# FlowSim 一维水动力仿真系统 — 技术指南

> 本文档基于 `FlowSimApp/Models/` 目录下的 C# 源代码，系统讲解**河道断面几何与水力计算**、**断面插值算法**、**一维圣维南方程组求解**三大主题的原理、数学公式、类结构及调用步骤。

---

## 目录

1. [总体架构](#总体架构)
2. [河道断面（CrossSection）](#一河道断面-crosssection)
   - 2.1 [抽象基类 `CrossSection`](#21-抽象基类-crosssection)
   - 2.2 [梯形断面 `TrapezoidalSection`](#22-梯形断面-trapezoidalsection)
   - 2.3 [不规则断面 `IrregularSection`](#23-不规则断面-irregularsection)
   - 2.4 [断面插值 `CrossSectionInterpolator`](#24-断面插值-crosssectioninterpolator)
3. [水力工具函数（Hydraulics）](#二水力工具函数-hydraulics)
4. [边界条件（Boundary）](#三边界条件-boundary)
5. [集总调蓄库（LumpedStorage）](#四集总调蓄库-lumpedstorage)
6. [河道模型（Channel）](#五河道模型-channel)
7. [求解器（Solver / PreissmannSolver / LaxSolver）](#六求解器)
   - 7.1 [基类 `Solver`](#71-基类-solver)
   - 7.2 [Preissmann 隐式格式](#72-preissmann-隐式格式)
   - 7.3 [Lax-Friedrichs 显式格式](#73-lax-friedrichs-显式格式)
8. [完整调用示例](#八完整调用示例)
9. [核心公式速查表](#九核心公式速查表)

---

## 总体架构

```
FlowSimApp/Models/
├── CrossSection.cs      # 断面几何：TrapezoidalSection、IrregularSection、插值器
├── Hydraulics.cs        # 水力工具：Manning公式、弗劳德数、Brent求根、插值、积分
├── Boundary.cs          # 边界条件：流量过程线、固定水深、正常水深、水位流量关系
├── Hydrograph.cs        # 过程线：时间序列插值 / 解析函数
├── RatingCurve.cs       # 水位流量关系：多项式/幂律拟合与查询
├── LumpedStorage.cs     # 集总调蓄库：质量守恒 + 能量损失
├── Channel.cs           # 河道：初始条件、断面网格、曲率计算
├── Solver.cs            # 求解器基类：结果后处理、Excel输出
├── PreissmannSolver.cs  # Preissmann 隐式有限差分格式
└── LaxSolver.cs         # Lax-Friedrichs 显式有限差分格式
```

数据流向：

```
用户构造 Boundary + CrossSection
        ↓
    构造 Channel（设置断面、坐标、初始条件）
        ↓
    构造 PreissmannSolver / LaxSolver
        ↓
    solver.Run()  →  逐时间层推进圣维南方程组
        ↓
    solver.SaveResults()  →  输出 Excel + 文本摘要
```

---

## 一、河道断面 (CrossSection)

河道断面描述某一横剖面的几何形状，提供过水面积 $A$、湿周 $P$、水力半径 $R$、水面宽 $T$ 等水力参数，以及它们对水深/流量的导数（用于隐式格式的 Jacobi 矩阵）。

### 2.1 抽象基类 `CrossSection`

**文件**：`CrossSection.cs`，命名空间 `FlowSim.Models`

```csharp
public abstract class CrossSection
{
    public double NLeft, NMain, NRight;          // 左滩区/主槽/右滩区曼宁糙率
    public double LeftFpLimit, RightFpLimit;     // 复式断面分区横坐标界限
    public double Curvature;                     // 弯道曲率 κ = 1/R_c（1/m）
    public double? BedSlope;                     // 河床纵坡 S₀（正值=下坡）

    // 子类必须实现：
    public abstract double ZMin { get; }         // 床底最低高程（m）
    public abstract (double A, double P, double R, double T) Properties(double hw);
    public abstract double Conveyance(double hw);
    public abstract double DConveyance_DA(double hw);
    public abstract double DRadius_DA(double hw);
    public abstract double DArea_Dh(double hw);
    public abstract double ZAt(double x);        // 横坐标 x 处的床底高程
    public abstract double GetEquivalentN(double hw);
}
```

基类提供的共享计算方法：

| 方法 | 公式 | 说明 |
|------|------|------|
| `FrictionSlope(h, Q)` | $S_f = Q\|Q\|/K^2$ | 曼宁摩阻坡度 |
| `DFrictionSlope_DA(h, Q)` | $\partial S_f/\partial A = -2S_f \cdot dK/dA / K$ | Jacobi 矩阵用 |
| `DFrictionSlope_DQ(h, Q)` | $\partial S_f/\partial Q = 2\|Q\|/K^2$ | Jacobi 矩阵用 |
| `CurvatureSlope(h, Q)` | $S_c = \frac{(2.86\sqrt{f}+2.07f)h^2Fr^2}{(0.565+\sqrt{f})r_c^2}$ | 弯道二次流修正 |
| `NormalDepth(Qtarget)` | Brent 法求解 $Q_n(h)=Q_{target}$ | 均匀流正常水深 |

#### 糙率参数批量存取

```csharp
// 获取当前糙率设置
var (nL, nM, nR, lLim, rLim) = xs.GetRoughnessPara();

// 批量设置（用于子槽处理时传递糙率）
xs.SetRoughnessPara((0.025, 0.030, 0.025, -15.0, 15.0));
```

---

### 2.2 梯形断面 `TrapezoidalSection`

支持三种断面形态：

| 类型 | 条件 | 描述 |
|------|------|------|
| 矩形 | `mMain == 0`，非复式 | 简单矩形过水断面 |
| 简单梯形 | `zBank == null` | 主槽梯形，无滩区 |
| 复式梯形 | `zBank != null` | 主槽 + 左右梯形滩区 |

#### 构造函数

```csharp
// 简单梯形（向后兼容）
var xs = new TrapezoidalSection(
    bMain: 10.0,      // 底宽（m）
    mMain: 1.5,       // 边坡系数（1:1.5）
    zBed:  0.0,       // 床底高程（m）
    nMain: 0.030,     // 曼宁糙率
    bedSlope: 0.001   // 河床纵坡
);

// 复式梯形（含滩区）
var xs = new TrapezoidalSection(
    bMain: 10.0, mMain: 1.5, zBed: 0.0, nMain: 0.025,
    zBank: 3.0,         // 滩面高程（m）
    bFpLeft: 20.0,      // 左滩区底宽（m）
    bFpRight: 20.0,     // 右滩区底宽（m）
    mFp: 2.0,           // 滩区边坡
    nLeft: 0.040,       // 左滩糙率
    nRight: 0.040,      // 右滩糙率
    bedSlope: 0.001
);
```

#### 断面几何计算 — `Properties(hw)`

**水深** $h = hw - z_{bed}$（相对于床底的水深，单位：m）

**情形 1：矩形**（$m_{main}=0$，非复式）

$$A = b_{main} \cdot h,\quad P = b_{main} + 2h,\quad T = b_{main}$$

**情形 2：简单梯形**（非复式）

$$T = b_{main} + 2m_{main}h$$
$$A = \frac{b_{main}+T}{2} \cdot h = (b_{main} + m_{main}h)\cdot h$$
$$P = b_{main} + 2h\sqrt{1+m_{main}^2}$$

**情形 3：复式梯形**（$hw > z_{bank}$）

$$h_{fp} = hw - z_{bank} \quad\text{（滩区水深）}$$
$$\text{主槽（满槽）：}\; A_m = \frac{b_{main}+T_{bank}}{2}\cdot h_{bk},\quad P_m = b_{main}+2h_{bk}\sqrt{1+m_m^2}$$
$$\text{左滩区：}\; A_L = \left(b_{fpL} + \tfrac{1}{2}m_{fp}h_{fp}\right)h_{fp},\quad P_L = b_{fpL}+h_{fp}\sqrt{1+m_{fp}^2}$$
$$\text{右滩区（同左）：}\; A_R,\ P_R \text{（对称）}$$
$$A_{total}=A_m+A_L+A_R,\quad T_{total}=W_{bank}+2m_{fp}h_{fp}$$

其中 $T_{bank} = b_{main}+2m_m h_{bk}$，$W_{bank}=b_{fpL}+T_{bank}+b_{fpR}$。

#### 等效糙率 — Lotter 方法

对复式断面（$hw > z_{bank}$）采用 Horton-Einstein（Lotter）方法：

$$K_{total} = \left(K_L^{1.5} + K_M^{1.5} + K_R^{1.5}\right)^{2/3}$$

等效曼宁糙率：

$$n_{eq} = \frac{A_{total} \cdot R_{total}^{2/3}}{K_{total}}$$

```csharp
double n_eq = xs.GetEquivalentN(hw);
```

#### 水力半径导数 — `DRadius_DA(hw)`

由 $R = A/P$ 对面积 $A$ 求导（链式法则）：

$$\frac{dR}{dA} = \frac{P - A\cdot\frac{dP}{dA}}{P^2},\quad \frac{dP}{dA} = \frac{dP/dh}{dA/dh} = \frac{dP/dh}{T}$$

各情形 $dP/dh$：

| 情形 | $dP/dh$ |
|------|---------|
| 矩形 | $2$ |
| 简单梯形 | $2\sqrt{1+m_m^2}$ |
| 复式（水上漫滩）| $2\sqrt{1+m_{fp}^2}$ |

---

### 2.3 不规则断面 `IrregularSection`

由一组横坐标-高程折线点 $(x_k, z_k)$ 定义，自动按 $x$ 升序排列。

#### 构造函数

```csharp
var xs = new IrregularSection(
    x: new[] { 0.0, 5.0, 10.0, 15.0, 20.0, 25.0 },
    z: new[] { 5.0, 3.0,  0.0,  0.0,  3.0,  5.0 },
    n: 0.030,
    bedSlope: 0.0005
);
```

#### 断面几何计算 — `Properties(hw)`

**核心算法**：识别"湿润区段"并分段积分。

**步骤 1**：计算各节点水深 $h_k = hw - z_k$，标记湿润节点（$h_k > 0$）。

**步骤 2**：识别连续湿润区段（`FindWetSegments`），并通过线性插值确定水面与折线的交叉点（`BuildSegment`）：

$$x_{cross} = x_{k-1} + \frac{hw - z_{k-1}}{z_k - z_{k-1}}(x_k - x_{k-1})$$

**步骤 3**：对每个湿润区段，用梯形积分计算面积：

$$A = \sum_{k} \frac{d_k + d_{k+1}}{2}(x_{k+1}-x_k), \quad d_k = \max(hw - z_k, 0)$$

湿周（勾股定理）：

$$P = \sum_k \sqrt{(x_{k+1}-x_k)^2 + (z_{k+1}-z_k)^2}$$

水面宽（区段两端横坐标差）：

$$T = x_{最右端} - x_{最左端}$$

#### 多子槽摩阻坡度 — `FrictionSlope` 重写

当折线断面存在干坝（部分区段高程超过水位），水流被分割为多个互不相连的子槽时，采用 Horton-Einstein 方法组合各子槽输水能力：

```
K_combined = (ΣK_j^1.5)^(2/3)
Sf = Q|Q| / K_combined²
```

C# 实现（内部调用 `GetSubchannels(hw)`）：

```csharp
// 自动识别子槽，GetSubchannels() 返回各子槽的 (x[], z[]) 折线
private (double[] x, double[] z)[] GetSubchannels(double hw)
// 一个子槽时，退回基类的单槽计算；多子槽时采用组合输水能力
public override double FrictionSlope(double h, double Q)
```

`dSf_dA` 和 `dSf_dQ` 的多子槽导数：

$$\frac{dK_{combined}}{dA} = \frac{2}{3}K_{sum}^{-1/3}\sum_j 1.5\,K_j^{0.5}\frac{dK_j}{dA}$$

#### 数值导数 — `DRadius_DA(hw)`

采用中心差分（步长 $\delta h = 10^{-6}$ m）：

$$\frac{dR}{dA} \approx \frac{R(hw+\delta h) - R(hw-\delta h)}{A(hw+\delta h) - A(hw-\delta h)}$$

---

### 2.4 断面插值 `CrossSectionInterpolator`

在两个相邻断面之间按距离权重线性插值，生成中间节点的断面。

**权重规则**：距哪个断面近，权重越大：

$$w_1 = \frac{d_2}{d_1+d_2},\quad w_2 = \frac{d_1}{d_1+d_2}$$

其中 $d_1$ 为插值点到左断面的距离，$d_2$ 为到右断面的距离。

#### 调用方式

```csharp
CrossSection interp = CrossSectionInterpolator.Interpolate(
    xs1,     // 上游断面
    xs2,     // 下游断面
    dist1,   // 插值点到 xs1 的距离（m）
    dist2    // 插值点到 xs2 的距离（m）
);
```

#### 三种插值情形

**情形 A：两者均为梯形断面**

对底宽 $b$、边坡 $m$、床底高程 $z$、糙率 $n$、坡度 $S_0$、弯曲度 $\kappa$、复式参数（$z_{bank}$、$b_{fp}$、$m_{fp}$、$n_L$、$n_R$）均进行线性加权插值：

```csharp
double b = t1.Width * w1 + t2.Width * w2;
double m = t1.MMain * w1 + t2.MMain * w2;
double z = t1.ZMin  * w1 + t2.ZMin  * w2;
// ...复式参数同理...
return new TrapezoidalSection(b, m, z, n, ...);
```

**情形 B：两者均为不规则断面**

取两断面 $X$ 坐标的并集作为主坐标轴，对高程值线性插值：

```csharp
double[] xMaster = Union(ir1.X, ir2.X);   // X 坐标并集
double[] zNew = xMaster.Select(x =>
    ir1.ZAt(x) * w1 + ir2.ZAt(x) * w2).ToArray();
// 同时插值并设置复合糙率参数
```

**情形 C：梯形 + 不规则（混合类型）**

统一用 `ZAt(x)` 接口在两断面上查询高程，创建 `IrregularSection` 结果：

```csharp
double[] z1 = xMaster.Select(xs1.ZAt).ToArray();
double[] z2 = xMaster.Select(xs2.ZAt).ToArray();
double[] zNew = z1.Zip(z2, (a, b) => a * w1 + b * w2).ToArray();
```

---

## 二、水力工具函数 (Hydraulics)

**文件**：`Hydraulics.cs`，静态类

### 曼宁公式体系

| 函数 | 公式 | 说明 |
|------|------|------|
| `Conveyance(A, n, R)` | $K = \frac{A \cdot R^{2/3}}{n}$ | 输水能力 |
| `DConveyance_DA(A, n, R, dR_dA)` | $\frac{dK}{dA}=\frac{R^{2/3}+\frac{2}{3}AR^{-1/3}dR/dA}{n}$ | K 对面积的导数 |
| `FrictionSlope(Q, K)` | $S_f = Q\|Q\|/K^2$ | 摩阻坡度 |
| `DFrictionSlope_DA(Q, K, dK_dA)` | $-2S_f \cdot dK/dA / K$ | ∂Sf/∂A |
| `DFrictionSlope_DQ(Q, K)` | $2\|Q\|/K^2$ | ∂Sf/∂Q |
| `NormalFlow(S0, K)` | $Q_n = K\sqrt{\|S_0\|}$ | 正常流量 |

### 弗劳德数

$$Fr = \frac{V}{\sqrt{gD}},\quad V=Q/A,\quad D=A/T$$

```csharp
double fr = Hydraulics.FroudeNumber(T, A, Q);
double dFr_dA = Hydraulics.DFroude_DA(T, A, Q);   // 偏导数，Jacobi 矩阵用
double dFr_dQ = Hydraulics.DFroude_DQ(T, A);
```

### 弯曲坡度（弯道二次流修正）

$$S_c = \frac{(2.86\sqrt{f}+2.07f)\,h^2 Fr^2}{(0.565+\sqrt{f})\,r_c^2}$$

其中 $f$ 为 Darcy-Weisbach 摩阻系数，$r_c=1/\kappa$ 为弯道曲率半径：

$$f = \frac{8g}{C^2},\quad C = \frac{R^{1/6}}{n}$$

### 数值方法工具

| 函数 | 说明 |
|------|------|
| `Brentq(f, a, b)` | Brent 方法求解 $f(x)=0$，结合二分/割线/逆二次插值 |
| `Interp(x, xs, ys)` | 有序数组线性插值（二分查找，超出范围夹紧） |
| `Gradient(y, x)` | 数值梯度（端点前向/后向差分，内部中心差分） |
| `Trapezoid(y, x)` | 梯形法则数值积分 |

---

## 三、边界条件 (Boundary)

**文件**：`Boundary.cs`

### 边界条件类型

| 枚举值 | 说明 | 未知量 |
|--------|------|--------|
| `FlowHydrograph` | 流量过程线 $Q(t)$ | $Q$（已知），解算 $h$ |
| `NormalDepth` | 正常水深（均匀流） | $Q = K\sqrt{S_0}$ |
| `RatingCurve` | 水位流量关系曲线 | $Q = f(Z)$ |
| `FixedDepth` | 固定水深或集总调蓄 | $h$（已知），解算 $Q$ |
| `StageHydrograph` | 水位过程线 $Z(t)$ | $h = Z(t) - z_{bed}$ |

### 边界残差方程

Preissmann 求解器在每次牛顿迭代中调用：

```csharp
double r = boundary.ConditionResidual(depth, flow, time, duration, volIn);
// 当残差 r = 0 时，边界约束被满足
```

**流量类**（`IsFlowDependent = true`）：残差 $= Q - Q_{target}$

**水深类**（`IsFlowDependent = false`）：残差 $= h - h_{target}$

### 偏导数（Jacobi 矩阵）

```csharp
double df_dh = boundary.Df_Dh(depth, flowRate, time);     // ∂f/∂h
double df_dQ = boundary.Df_DQ(depth, flowRate, dt, t, vol); // ∂f/∂Q
```

### 含集总调蓄库的 FixedDepth 边界

当下游关联 `LumpedStorage` 时，通过质量守恒动态计算库水位，并叠加进口能量损失：

$$Z_{interface} = Z_{reservoir} + h_{loss}$$
$$h_{target} = Z_{interface} - z_{bed}$$

---

## 四、集总调蓄库 (LumpedStorage)

**文件**：`LumpedStorage.cs`

将水库或滞洪区简化为"集总"零维单元，通过质量守恒方程求解每时步的库水位。

### 质量守恒方程

$$\Delta V(Y_{old},Y_{new}) = V_{in} - \frac{Q_{out}(Y_{old})+Q_{out}(Y_{new})}{2}\cdot\Delta t$$

用 Brent 方法在 $[Y_{min}, Y_{max}]$ 范围内求解 $Y_{new}$：

```csharp
double yNew = ls.MassBalance(duration, volIn, yOld, time);
```

### 面积-水位关系

| 模式 | 描述 |
|------|------|
| 常数面积 | `SurfaceArea`（矩形库等效） |
| 面积曲线 | `SetAreaCurve(table)` 后插值 |

```csharp
ls.SetAreaCurve(new double[,] {
    { 10.0, 5_000.0 },   // {水位(m), 面积(m²)}
    { 11.0, 8_000.0 },
    { 12.0, 12_000.0 }
}, alpha: 1.0, beta: 0.0);
```

面积对水位导数（用于 Jacobi 矩阵）：

```csharp
double dA_dY = ls.DAdy(stage);   // dA/dY，数值梯度插值
```

### 能量损失（可选）

当 `CaptureLosses = true` 时，计算进口处的三种损失：

- **摩阻损失**：$h_f = S_f \cdot L_{res}$（Manning 公式）
- **扩散（突扩）损失**：$h_{exp} = K_{exp}\frac{V^2}{2g}$，$K_{exp}=(1-A_{in}/A_{res})^2$
- **经验损失**：$h_{emp} = K_Q\frac{V^2}{2g}$

```csharp
ls.CaptureLosses = true;
ls.ReservoirLength = 500.0;   // 水库等效长度（m）
ls.KQ = 0.1;                  // 经验系数
```

---

## 五、河道模型 (Channel)

**文件**：`Channel.cs`

### 构造函数

```csharp
var channel = new Channel(
    upstreamBoundary: usBound,
    downstreamBoundary: dsBound,
    initialFlow: 100.0,            // 初始流量（m³/s）
    roughness: 0.030,              // 统一糙率（无自定义断面时使用）
    width: 20.0,                   // 河道宽度（m）
    initMethod: InitializationMethod.GVFEquation  // 初始化方法
);
```

### 初始化方法

| 枚举值 | 原理 | 适用场景 |
|--------|------|----------|
| `Linear` | 上下游水深线性插值 | 简单快速，精度较低 |
| `GVFEquation` | 从下游向上游积分渐变流（GVF）方程 | 常规顺坡缓流 |
| `SteadyState` | 各节点求解正常水深（均匀流） | 坡度均匀的长直河道 |

#### GVF 方程积分（Heun 预测-修正法）

$$\frac{dh}{dx} = \frac{S_0 - S_f}{1 - Fr^2}$$

从下游（$i=N-1$）向上游（$i=0$）逐节点积分：

1. **预测**：$h_{pred} = h_{down} - (dh/dx)_{i+1} \cdot \Delta x$
2. **修正**：$h_{up} = h_{down} - \frac{(dh/dx)_{i+1}+(dh/dx)_{pred}}{2}\cdot\Delta x$（Heun 法，二阶精度）

当 $Fr \geq 1$（超临界流）时，分母趋于零，此时梯度设为 0 跳过。

### 设置自定义断面

```csharp
channel.SetCrossSection(
    chainages: new[] { 0.0, 500.0, 1000.0 },   // 断面桩号（m）
    sections: new CrossSection[] { xs0, xs1, xs2 }
);
```

### 设置水平坐标（弯曲曲率计算）

```csharp
channel.SetCoords(
    coords: new double[,] {          // [n, 2] 中心线坐标（m）
        { 0, 0 }, { 100, 10 }, { 200, 0 }, { 300, -10 }, { 400, 0 }
    },
    chainages: new[] { 0.0, 100.0, 200.0, 300.0, 400.0 }  // 对应桩号
);
```

调用 `InitializeConditions(nNodes)` 后自动计算各断面处的弯道曲率：

$$\kappa_i = \frac{2\sin(\theta/2)}{L}\cdot\text{sign}(\vec{v_1}\times\vec{v_2})$$

其中 $\theta$ 为相邻方向向量的转向角，$L$ 为平均弦长，叉积符号决定左转/右转。

### 节点断面插值

`InitializeConditions(nNodes)` 内部流程：

```
1. _createProvisionalCrossSections()  ← 若未提供自定义断面，生成临时梯形断面
2. _calcCurvature()                   ← 若已设置坐标，计算各断面弯曲曲率
3. ChAtNode = Linspace(us.Chainage, ds.Chainage, nNodes)  ← 均匀节点桩号
4. _interpolateCrossSections()        ← 对每个节点桩号插值断面
   ├─ s ≤ 第一个断面桩号 → 直接用第一个断面
   ├─ s ≥ 最后断面桩号  → 直接用最后断面
   └─ 二分查找区间 [j, j+1]，调用 CrossSectionInterpolator.Interpolate(...)
5. 计算初始条件（水深/流量）并写入 InitialConditions[nNodes, 2]
```

---

## 六、求解器

### 7.1 基类 `Solver`

所有求解器的公共部分：参数存储、结果数组、后处理和输出。

#### 构造参数

```csharp
// Preissmann 求解器构造示例
var solver = new PreissmannSolver(
    channel: channel,
    theta: 0.6,               // Preissmann 权重（0.5~1.0）
    timeStep: 60.0,           // 时间步长（秒）
    spatialStep: 500.0,       // 目标空间步长（m）
    simulationTime: 86400.0   // 总模拟时长（秒）
);
```

#### 结果数组（`PrepareResults()` 后可用）

| 属性 | 维度 | 说明 |
|------|------|------|
| `Flow[k, i]` | [时间层, 节点] | 流量（m³/s） |
| `Depth[k, i]` | [时间层, 节点] | 水深（m） |
| `Level[k, i]` | [时间层, 节点] | 绝对水位（m） |
| `Area[k, i]` | [时间层, 节点] | 过水面积（m²） |
| `TopWidth[k, i]` | [时间层, 节点] | 水面宽（m） |
| `Velocity[k, i]` | [时间层, 节点] | 流速（m/s） |
| `WaveCelerity[k, i]` | [时间层, 节点] | 波速 $V+\sqrt{gD}$（m/s） |
| `FroudeNumber[k, i]` | [时间层, 节点] | 弗劳德数 |
| `Amplitude[k, i]` | [时间层, 节点] | 相对初始水深的变化量（m） |
| `PeakAmplitude[i]` | [节点] | 各节点历史最大振幅（m） |
| `StorageStage[k]` | [时间层] | 调蓄库水位（m），可选 |
| `StorageOutflow[k]` | [时间层] | 调蓄库出口流量（m³/s），可选 |

#### 结果输出

```csharp
solver.SaveResults(
    folderPath: @"C:\Results",
    fileName:   "simulation_run1.xlsx"
);
```

输出内容：

- **Excel 文件**（`.xlsx`）：Level、Flow、Depth、Velocity、Area、Top width、Wave celerity、Amplitude、Froude number、Peak amplitude、Bed level，以及可选的 Outflow（调蓄库）和 Reservoir stage（Preissmann）。
- **文本摘要**（`.txt`）：空间/时间步长、θ 值（Preissmann）、模拟时长、质量不平衡百分比、峰值流量、流量衰减率、中间体积传播时间。

---

### 7.2 Preissmann 隐式格式

**文件**：`PreissmannSolver.cs`

#### 数学原理

Preissmann 格式将圣维南方程中的时间导数和空间导数用加权差分近似：

**时间差分算子**（`TimeDiff`）：

$$\frac{\partial f}{\partial t} \approx \frac{\bar{f}^{n+1} - \bar{f}^n}{\Delta t} = \frac{(f_i^{n+1}+f_{i+1}^{n+1}) - (f_i^n+f_{i+1}^n)}{2\Delta t}$$

**空间差分算子**（`SpatialDiff`）：

$$\frac{\partial f}{\partial x} \approx \theta\frac{f_{i+1}^{n+1}-f_i^{n+1}}{\Delta x} + (1-\theta)\frac{f_{i+1}^n-f_i^n}{\Delta x}$$

**加权单元均值算子**（`CellAvg`）：

$$\bar{f} = \frac{\theta}{2}(f_i^{n+1}+f_{i+1}^{n+1}) + \frac{1-\theta}{2}(f_i^n+f_{i+1}^n)$$

**参数 θ 的影响**：

| θ 值 | 格式名称 | 精度 | 稳定性 |
|------|---------|------|--------|
| 0.5 | Crank-Nicolson | 二阶时间精度 | 无条件稳定（弱耗散） |
| 1.0 | 全隐式（Euler 后退）| 一阶精度 | 无条件稳定（强耗散） |
| 0.6～0.8 | 折中选取 | 接近二阶 | 推荐工程使用 |

#### 方程组结构

每个时间步形成 $2N$ 个方程，未知量向量 $\mathbf{x} = [h_0, Q_0, h_1, Q_1, \ldots, h_{N-1}, Q_{N-1}]^\top$：

$$\mathbf{R}(\mathbf{x}) = \begin{bmatrix}
f_{upstream}(h_0, Q_0) \\
R_{c,0}(h_0, Q_0, h_1, Q_1) \\
R_{m,0}(h_0, Q_0, h_1, Q_1) \\
R_{c,1}(\cdots) \\
R_{m,1}(\cdots) \\
\vdots \\
f_{downstream}(h_{N-1}, Q_{N-1})
\end{bmatrix} = \mathbf{0}$$

**连续性方程残差**（单元 $i$）：

$$R_c^{(i)} = \frac{\partial A}{\partial t}\bigg|_i + \frac{\partial Q}{\partial x}\bigg|_i = 0$$

**动量方程残差**（单元 $i$）：

$$R_m^{(i)} = \frac{\partial Q}{\partial t}\bigg|_i + \frac{\partial(Q^2/A)}{\partial x}\bigg|_i + g\bar{A}\left(\frac{\partial Y}{\partial x}\bigg|_i + \overline{S_e}\right) = 0$$

其中 $S_e = S_f + S_c$（摩阻坡度 + 弯曲坡度）。

#### 牛顿-拉弗森迭代

```
for each time step k:
    x = 上一时层的未知量作为初始猜测
    repeat:
        写入 Depth[k,:] 和 Flow[k,:]（当前估计值）
        计算残差向量 R（ComputeResidualVector）
        计算雅可比矩阵 J（ComputeJacobian，带状 2N×2N 矩阵）
        求解线性系统 J·Δ = −R（MathNet.Numerics）
        x += Δ
    until ||R||₂ < 1e-4  或  超过 100 次迭代
```

**雅可比矩阵结构**（带状稀疏，每行最多 4 个非零元）：

```
行 0      : [∂f_us/∂h₀  ∂f_us/∂Q₀  0  0  ...  0]
行 1(连续) : [∂Rc/∂h₀  ∂Rc/∂Q₀  ∂Rc/∂h₁  ∂Rc/∂Q₁  0  ...]
行 2(动量) : [∂Rm/∂h₀  ∂Rm/∂Q₀  ∂Rm/∂h₁  ∂Rm/∂Q₁  0  ...]
...
行 2N-1   : [... 0  ∂f_ds/∂h_{N-1}  ∂f_ds/∂Q_{N-1}]
```

雅可比偏导数（链式法则展开）：

```csharp
// 连续性方程偏导（线性算子，较简单）
J[row, 2*i]     = TimeDiff(k1_i: 1)   * DADhAt(k, i);     // ∂Rc/∂h_i
J[row, 2*i+1]   = SpatialDiff(k1_i: 1);                   // ∂Rc/∂Q_i
J[row, 2*(i+1)] = TimeDiff(k1_i1: 1)  * DADhAt(k, i+1);   // ∂Rc/∂h_{i+1}
J[row, 2*(i+1)+1] = SpatialDiff(k1_i1:1);                 // ∂Rc/∂Q_{i+1}

// 动量方程偏导（非线性，通过链式法则展开 Q²/A 项和 Se 项）
J[row+1, 2*i]   = DM_Dh_i(i);
J[row+1, 2*i+1] = DM_DQ_i(i);
// ...
```

---

### 7.3 Lax-Friedrichs 显式格式

**文件**：`LaxSolver.cs`

#### 数学原理

Lax-Friedrichs 格式是一阶精度显式格式，通过对相邻两节点取空间平均（引入数值耗散）来保持稳定性：

**连续性方程离散**：

$$A_i^{n+1} = \frac{A_{i-1}^n + A_{i+1}^n}{2} - \frac{\Delta t}{2\Delta x}(Q_{i+1}^n - Q_{i-1}^n)$$

**动量方程离散**：

$$Q_i^{n+1} = \frac{Q_{i-1}^n + Q_{i+1}^n}{2} - \Delta t\left[\frac{d(Q^2/A)}{dx} + g\bar{A}\left(\frac{dY}{dx} + \overline{S_e}\right)\right]$$

#### CFL 稳定性条件

每个时步完成后，对所有节点检验：

$$\text{CFL} = \frac{\max(|V+c|, |V-c|)}{\Delta x/\Delta t} \leq 1$$

其中 $c = \sqrt{gD}$（浅水波速），$V = Q/A$（流速）。

```csharp
private void CheckCflAll()
// 违反 CFL 条件时抛出 InvalidOperationException
```

#### 端节点处理（虚节点策略）

Lax 格式对端节点 $i=0$ 和 $i=N-1$ 需要虚节点 $i=-1$（上游）和 $i=N$（下游）：

| `LaxSecondaryBC` | 虚节点值 | 特性 |
|-----------------|---------|------|
| `Constant` | = 端节点值（零阶外推）| 简单，引入较大误差 |
| `Mirror` | = 相邻内节点值（反射）| 适合无梯度边界 |
| `Linear` | = $2×$端节点 $-$ 相邻内节点（外推）| 一阶外推 |

**端节点更新策略**：

- **流量类边界（`IsFlowDependent`）**：用 Lax 格式更新面积（→水深），流量从边界条件直接获取。
- **水深类边界**：用 Lax 格式更新流量，水深从边界条件（固定水深/集总调蓄）确定。

#### 面积反算水深（`AreaToDepth`）

Lax 格式以面积为状态变量，需将新面积转换回水深。采用 Brent 方法求解：

$$A(h + z_{min}) = A_{target}$$

---

## 八、完整调用示例

### 示例 1：Preissmann 格式 + 简单梯形断面

```csharp
using FlowSim.Models;

// 1. 定义过程线（流量入流边界）
var inflow = new Hydrograph();
inflow.SetTable(new double[,] {
    { 0,      100 },
    { 3600,   500 },
    { 7200,   800 },
    { 14400,  300 },
    { 86400,  100 }
});

// 2. 定义边界条件
var usBound = new Boundary(
    condition: BoundaryConditionType.FlowHydrograph,
    chainage: 0.0,
    bedLevel: 10.0,
    initialDepth: 1.5,
    hydrograph: inflow
);

var dsBound = new Boundary(
    condition: BoundaryConditionType.NormalDepth,
    chainage: 10000.0,
    bedLevel: 5.0,
    initialDepth: 1.5
);

// 3. 定义断面
var usXs = new TrapezoidalSection(bMain: 20.0, mMain: 1.5, zBed: 10.0, nMain: 0.030);
var dsXs = new TrapezoidalSection(bMain: 20.0, mMain: 1.5, zBed:  5.0, nMain: 0.030);
double bedSlope = (10.0 - 5.0) / 10000.0;
usXs.BedSlope = bedSlope;
dsXs.BedSlope = bedSlope;

// 4. 创建河道并设置断面
var channel = new Channel(usBound, dsBound, initialFlow: 100.0,
                          initMethod: InitializationMethod.GVFEquation);
channel.SetCrossSection(
    chainages: new[] { 0.0, 10000.0 },
    sections: new CrossSection[] { usXs, dsXs }
);

// 5. 创建求解器并运行
var solver = new PreissmannSolver(
    channel: channel,
    theta: 0.6,
    timeStep: 300.0,           // 5 分钟步长
    spatialStep: 1000.0,       // 1 km 空间步长
    simulationTime: 86400.0    // 24 小时
);

solver.Run(verbose: 1);

// 6. 输出结果
solver.SaveResults(@"C:\FlowSim\Results", "channel_flood.xlsx");
Console.WriteLine($"Peak depth at outlet: {solver.Depth![^1, ^1]:F2} m");
```

### 示例 2：Lax 格式 + 不规则断面 + 弯道修正

```csharp
// 不规则断面（U 形河道）
var xs = new IrregularSection(
    x: new[] { 0.0, 5.0, 10.0, 15.0, 20.0, 30.0, 40.0 },
    z: new[] { 5.0, 4.0,  2.0,  2.0,  4.0,  5.5,  6.0 },
    n: 0.025,
    bedSlope: 0.0002
);
// 设置复式糙率（左滩/主槽/右滩）
xs.SetRoughnessPara((0.040, 0.025, 0.040, 5.0, 25.0));

// 河道水平坐标（弯曲河道）
double[,] coords = { {0,0}, {1000,50}, {2000,0}, {3000,-50}, {4000,0} };
double[] coordChs = { 0, 1000, 2000, 3000, 4000 };
channel.SetCoords(coords, coordChs);

// Lax 求解器
var laxSolver = new LaxSolver(
    channel: channel,
    timeStep: 10.0,
    spatialStep: 200.0,
    simulationTime: 3600.0,
    upstreamSecondaryBC: LaxSecondaryBC.Mirror,
    downstreamSecondaryBC: LaxSecondaryBC.Linear
);
laxSolver.Run(verbose: 0);
```

### 示例 3：含集总调蓄库的水库入库仿真

```csharp
// 水库出口水位流量关系曲线
var ratingCurve = new RatingCurve();
ratingCurve.Set(RatingCurveType.Power, a: 50.0, b: 1.8, stageShift: -100.0);

// 集总调蓄库
var reservoir = new LumpedStorage(yMin: 100.0, yMax: 115.0,
                                   minStage: 100.5, ratingCurve: ratingCurve);
reservoir.SetAreaCurve(new double[,] {
    { 100, 1_000_000 },
    { 105, 5_000_000 },
    { 110, 12_000_000 },
    { 115, 22_000_000 }
});
reservoir.CaptureLosses = true;
reservoir.ReservoirLength = 1000.0;

// 关联到下游边界
dsBound.SetLumpedStorage(reservoir);

// 运行 Preissmann 求解器后，SaveResults() 将自动输出:
// - "Outflow" 工作表（调蓄库出口流量）
// - "Reservoir stage" 工作表（库水位过程）
```

### 示例 4：RatingCurve 数据拟合

```csharp
var rc = new RatingCurve();

// 从实测数据拟合幂律曲线（Q = A·Z^B）
rc.Fit(
    discharges: new[] { 10.0, 50.0, 150.0, 400.0, 900.0 },
    stages:     new[] { 101.2, 102.5, 104.0, 106.0, 108.5 },
    type: RatingCurveType.Power,
    stageShift: -100.0   // 基准水位偏移
);

double Q = rc.Discharge(stage: 105.0);         // 由水位查流量
double Z = rc.Stage(discharge: 200.0);         // 由流量反查水位（牛顿迭代）
double dQ_dZ = rc.DQ_Dz(stage: 105.0);        // 导数（Jacobi 矩阵用）
```

---

## 九、核心公式速查表

### 断面水力参数

| 参数 | 符号 | 单位 | 说明 |
|------|------|------|------|
| 过水面积 | $A$ | m² | 水面以下的过水截面积 |
| 湿周 | $P$ | m | 水与床面接触的周长 |
| 水力半径 | $R = A/P$ | m | 反映水力效率 |
| 水面宽 | $T = dA/dh$ | m | 水面处断面宽度 |
| 水力深度 | $D = A/T$ | m | 用于弗劳德数计算 |
| 输水能力 | $K = AR^{2/3}/n$ | m³/s | Manning 公式核心 |

### 圣维南方程组

**连续性方程**：

$$\frac{\partial A}{\partial t} + \frac{\partial Q}{\partial x} = 0$$

**动量方程**：

$$\frac{\partial Q}{\partial t} + \frac{\partial(Q^2/A)}{\partial x} + gA\left(\frac{\partial Y}{\partial x} + S_e\right) = 0$$

其中 $Y = z_{bed} + h$（绝对水位），$S_e = S_f + S_c$（等效能量坡度）。

### Preissmann 差分算子（内部调用签名）

```csharp
// 时间差分：(新时层均值 - 旧时层均值) / Δt
double TimeDiff(double k1_i1, double k1_i, double k_i1, double k_i)
    => (k1_i1 + k1_i - k_i1 - k_i) / (2·Δt);

// 加权空间差分
double SpatialDiff(double k1_i1, double k1_i, double k_i1, double k_i)
    => θ·(k1_i1-k1_i)/Δx + (1-θ)·(k_i1-k_i)/Δx;

// 加权单元均值
double CellAvg(double k1_i1, double k1_i, double k_i1, double k_i)
    => 0.5·θ·(k1_i1+k1_i) + 0.5·(1-θ)·(k_i1+k_i);
```

### 各格式对比

| 格式 | 求解类型 | 稳定性 | 精度 | 每步计算量 | 适用场景 |
|------|---------|--------|------|-----------|---------|
| Preissmann (θ=0.5) | 隐式，Newton-Raphson | 无条件稳定 | 二阶 | $O(N)$ 线性系统 | 长时间模拟，大 Δt |
| Preissmann (θ=1.0) | 隐式，全耗散 | 无条件稳定 | 一阶 | $O(N)$ 线性系统 | 强耗散，抑制振荡 |
| Lax-Friedrichs | 显式 | CFL 限制 | 一阶 | $O(N)$ 显式计算 | 波速较慢，短时模拟 |

---

*文档版本：基于 FlowSimApp commit `d4f24ef`，2026-03-04。*
