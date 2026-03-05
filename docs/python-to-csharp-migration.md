# Python → C# 迁移对照表

> 本文档对照 `src/hydromodel/`（Python）与 `FlowSimApp/Models/`（C#）两套实现，
> 逐模块列出**文件、类、枚举、字段/属性、方法/函数**的对应关系，并附算法要点说明。
> C# 代码位于分支 `copilot/migrate-python-code-to-csharp`。

---

## 目录

1. [模块文件映射](#一模块文件映射)
2. [BoundaryConditionType 枚举](#二boundaryconditiontype-枚举)
3. [Boundary 类](#三boundary-类)
4. [CrossSection 抽象基类](#四crosssection-抽象基类)
5. [TrapezoidalSection 类](#五trapezoidalsection-类)
6. [IrregularSection 类](#六irregularsection-类)
7. [CrossSectionInterpolator / interpolate_cross_section](#七crosssectioninterpolator--interpolate_cross_section)
8. [Hydraulics 工具函数](#八hydraulics-工具函数)
9. [Hydrograph 类](#九hydrograph-类)
10. [RatingCurve 类](#十ratingcurve-类)
11. [LumpedStorage 类](#十一lumpedstorage-类)
12. [Channel 类](#十二channel-类)
13. [Solver 基类](#十三solver-基类)
14. [PreissmannSolver 类](#十四preissmannsolver-类)
15. [LaxSolver 类](#十五laxsolver-类)
16. [算法差异说明](#十六算法差异说明)

---

## 一、模块文件映射

| Python 文件 | C# 文件 | 说明 |
|---|---|---|
| `src/hydromodel/boundary.py` | `FlowSimApp/Models/Boundary.cs` | 边界条件 |
| `src/hydromodel/cross_section.py` | `FlowSimApp/Models/CrossSection.cs` | 水力断面（基类 + 梯形 + 不规则 + 插值器） |
| `src/hydromodel/hydraulics.py` | `FlowSimApp/Models/Hydraulics.cs` | 水力工具函数（静态类） |
| `src/hydromodel/hydrograph.py` | `FlowSimApp/Models/Hydrograph.cs` | 流量/水位过程线 |
| `src/hydromodel/rating_curve.py` | `FlowSimApp/Models/RatingCurve.cs` | 水位流量关系曲线 |
| `src/hydromodel/lumped_storage.py` | `FlowSimApp/Models/LumpedStorage.cs` | 集总调蓄库 |
| `src/hydromodel/channel.py` | `FlowSimApp/Models/Channel.cs` | 河道模型 |
| `src/hydromodel/solver.py` | `FlowSimApp/Models/Solver.cs` | 求解器基类 |
| `src/hydromodel/preissmann.py` | `FlowSimApp/Models/PreissmannSolver.cs` | Preissmann 隐式格式求解器 |
| `src/hydromodel/lax.py` | `FlowSimApp/Models/LaxSolver.cs` | Lax-Friedrichs 显式格式求解器 |
| `src/hydromodel/utility.py` | （内联至各模块） | 工具函数（字符串格式化、曲率计算等） |

---

## 二、BoundaryConditionType 枚举

**Python**：`boundary.py` 中使用字符串字面量，无显式枚举类型。  
**C#**：`Boundary.cs` 中定义 `public enum BoundaryConditionType`。

| Python 字符串值 | C# 枚举成员 | 含义 |
|---|---|---|
| `'flow_hydrograph'` | `FlowHydrograph` | 流量过程线 |
| `'fixed_depth'` | `FixedDepth` | 固定水深（可选集总调蓄） |
| `'normal_depth'` | `NormalDepth` | 正常水深（均匀流） |
| `'rating_curve'` | `RatingCurve` | 水位-流量关系曲线 |
| `'stage_hydrograph'` | `StageHydrograph` | 水位过程线 |

---

## 三、Boundary 类

### 3.1 字段 / 属性

| Python 属性 | C# 属性 | 类型 | 说明 |
|---|---|---|---|
| `self.condition` | `Condition` | `string` / `BoundaryConditionType` | 边界条件类型 |
| `self.cross_section` | `CrossSection` | `CrossSection?` | 关联的水力断面 |
| `self.bed_level` | `BedLevel` | `float?` / `double?` | 床底高程（m） |
| `self.initial_depth` | `InitialDepth` | `float?` / `double?` | 初始水深（m） |
| `self.initial_stage` | `InitialStage` | `float?` / `double?` | 初始水位 = BedLevel + InitialDepth |
| `self.chainage` | `Chainage` | `float` / `double` | 桩号（m） |
| `self.rating_curve` | `RatingCurve` | `RatingCurve?` | 水位流量关系曲线 |
| `self.hydrograph` | `Hydrograph` | `Hydrograph?` | 过程线 |
| `self.lumped_storage` | `LumpedStorage` | `LumpedStorage?` | 集总调蓄库 |

### 3.2 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(condition, chainage, bed_level, initial_depth, rating_curve, hydrograph)` | `Boundary(BoundaryConditionType condition, double chainage, double? bedLevel, double? initialDepth, RatingCurve? ratingCurve, Hydrograph? hydrograph)` | 构造函数 |
| `set_lumped_storage(lumped_storage)` | `SetLumpedStorage(LumpedStorage ls)` | 关联集总调蓄库 |
| `condition_residual(depth, flow, time, duration, vol_in)` | `ConditionResidual(double depth, double flow, double time, double duration, double volIn)` | 计算边界残差 |
| `df_dh(depth, flow_rate, time)` | `Df_Dh(double depth, double flowRate, double time)` | ∂f/∂h（对水深的偏导） |
| `df_dQ(depth, flow_rate, duration, time, vol_in)` | `Df_DQ(double depth, double flowRate, double duration, double time, double volIn)` | ∂f/∂Q（对流量的偏导） |
| `condition_type()` | `IsFlowDependent` （属性） | 判断边界方程是否以 Q 为未知量 |

### 3.3 算法说明

- Python 使用 `if self.condition == 'xxx'` 字符串比较；C# 使用 `switch (Condition)` 对枚举分支处理。
- Python 在构造时使用字符串验证合法性（`if self.condition not in [...]`）；C# 通过编译期枚举类型保证类型安全，无需运行时验证。
- 集总调蓄库时步历史水位存储：Python 使用 `list[-1]` 访问最后一个元素；C# 使用 `^1` 索引运算符（`[^1]`）。

---

## 四、CrossSection 抽象基类

### 4.1 字段 / 属性

| Python 属性 | C# 属性 | 类型 | 说明 |
|---|---|---|---|
| `self.n_left` | `NLeft` | `double` | 左侧滩区曼宁糙率 |
| `self.n_main` | `NMain` | `double` | 主槽曼宁糙率 |
| `self.n_right` | `NRight` | `double` | 右侧滩区曼宁糙率 |
| `self.left_fp_limit` | `LeftFpLimit` | `double` | 左侧洪泛区边界横坐标 |
| `self.right_fp_limit` | `RightFpLimit` | `double` | 右侧洪泛区边界横坐标 |
| `self.curvature` | `Curvature` | `double` | 弯道曲率 κ = 1/rc |
| `self.bed_slope` | `BedSlope` | `float?` / `double?` | 河床纵坡 S₀ |

### 4.2 抽象方法（子类必须实现）

| Python 抽象方法 | C# 抽象方法 | 说明 |
|---|---|---|
| `z_min` （property） | `ZMin` （abstract property） | 床底最低高程（m） |
| `width` （property） | `Width` （abstract property） | 断面代表宽度（m） |
| `properties(hw)` | `Properties(double hw)` | 返回 (A, P, R, T) 元组 |
| `get_equivalent_n(hw)` | `GetEquivalentN(double hw)` | 等效曼宁糙率 |
| `conveyance(hw)` | `Conveyance(double hw)` | 输水能力 K |
| `dK_dA(hw)` | `DConveyance_DA(double hw)` | dK/dA |
| `dR_dA(hw)` | `DRadius_DA(double hw)` | dR/dA |
| `dA_dh(hw)` | `DArea_Dh(double hw)` | dA/dh |
| `z_at(x)` | `ZAt(double x)` | 横坐标 x 处床底高程 |

### 4.3 具体方法（基类实现）

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `area(hw)` | `Area(double hw)` | 过水面积（委托 `Properties` 取 A） |
| `wetted_perimeter(hw)` | `WettedPerimeter(double hw)` | 湿周（委托取 P） |
| `hydraulic_radius(hw)` | `HydraulicRadius(double hw)` | 水力半径（委托取 R） |
| `top_width(hw)` | `TopWidth(double hw)` | 水面宽（委托取 T） |
| `friction_slope(h, Q)` | `FrictionSlope(double h, double Q)` | 摩阻坡度 Sf = Q\|Q\|/K² |
| `dSf_dA(h, Q)` | `DFrictionSlope_DA(double h, double Q)` | ∂Sf/∂A |
| `dSf_dQ(h, Q)` | `DFrictionSlope_DQ(double h, double Q)` | ∂Sf/∂Q |
| `curvature_slope(h, Q)` | `CurvatureSlope(double h, double Q)` | 弯曲坡度 Sc |
| `dSc_dA(h, Q)` | `DCurvatureSlope_DA(double h, double Q)` | ∂Sc/∂A |
| `dSc_dQ(h, Q)` | `DCurvatureSlope_DQ(double h, double Q)` | ∂Sc/∂Q |
| `normal_flow(hw)` | `NormalFlow(double hw)` | 正常流量 Qn = K√S₀ |
| `normal_depth(Q_target, hw_max)` | `NormalDepth(double Qtarget, double hwMax)` | Brent 法求正常水深 |
| `get_roughness_para()` | `GetRoughnessPara()` | 批量读取糙率参数元组 |
| `set_roughness_para(parameters)` | `SetRoughnessPara((...) parameters)` | 批量写入糙率参数 |

### 4.4 算法说明

- C# 基类新增**结果缓存**（`_lastHw` / `_lastRes`）：对同一水位重复调用 `Properties()` 时直接返回缓存值，避免重复计算。Python 无此优化。
- `dA_dh` 在 Python 中为独立抽象方法；C# 中保持一致但拼写为 `DArea_Dh`。

---

## 五、TrapezoidalSection 类

### 5.1 构造参数

| Python 参数 | C# 参数 | 说明 |
|---|---|---|
| `z_bed` | `zBed` | 床底高程（m） |
| `b_main` | `bMain` | 底宽（m） |
| `m_main` | `mMain` | 边坡系数（水平/竖直） |
| `n_main` | `nMain` | 主槽曼宁糙率 |
| `bed_slope` | `bedSlope` | 河床纵坡（可选） |
| `curvature` | `curvature` | 弯道曲率（可选） |
| `z_bank` | `zBank` | 滩面高程（可选，启用复式断面） |
| `b_fp_left` | `bFpLeft` | 左滩区底宽（m） |
| `b_fp_right` | `bFpRight` | 右滩区底宽（m） |
| `m_fp` | `mFp` | 滩区外侧边坡系数 |
| `n_left` | `nLeft` | 左滩区糙率 |
| `n_right` | `nRight` | 右滩区糙率 |

### 5.2 公开属性（只读）

| Python 属性 | C# 属性 | 说明 |
|---|---|---|
| `self.b_main` | `BMain` | 底宽 |
| `self.m_main` | `MMain` | 边坡系数 |
| `self.is_compound` | `IsCompound` | 是否复式断面 |
| `self.z_bank` | `ZBank` | 滩面高程 |
| `self.b_fp_left` | `BFpLeft` | 左滩区底宽 |
| `self.b_fp_right` | `BFpRight` | 右滩区底宽 |
| `self.m_fp` | `MFp` | 滩区外侧边坡系数 |

### 5.3 算法说明

- 矩形断面（`m_main == 0` 且非复式）：Python 用通用公式处理；C# 用 `_isRect` 标志启用简化公式分支，避免除以零。
- 复式断面子区分割：Python 在 `_get_subsection_props()` 中分左/主/右三子槽并用等效糙率合并；C# 结构相同，通过私有方法 `_SubsectionProps()` 返回元组并合并。

---

## 六、IrregularSection 类

### 6.1 字段 / 属性

| Python 属性 | C# 属性 | 类型 | 说明 |
|---|---|---|---|
| `self.x` | `X` | `double[]` | 断面横坐标数组（m） |
| `self.z` | `Z` | `double[]` | 对应床底高程数组（m） |
| `self._z_min`（私有） | `_zMin`（私有） | `double` | 断面最低高程 |
| `self._width`（私有） | `_width`（私有） | `double` | 总宽度 = X.max − X.min |

### 6.2 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(x, z, **kwargs)` | `IrregularSection(double[] x, double[] z, double n, ...)` | 构造，数组排序校验 |
| `properties(hw)` | `Properties(double hw)` | 数值积分计算 A、P、R、T |
| `get_subchannels(hw)` | `GetSubchannels(double hw)` （私有） | 查找过水子区间 |
| `get_equivalent_n(hw)` | `GetEquivalentN(double hw)` | 按子槽糙率加权等效 |
| `conveyance(hw)` | `Conveyance(double hw)` | 子槽输水能力求和 |
| `dK_dA(hw)` | `DConveyance_DA(double hw)` | 数值差分 dK/dA |
| `dR_dA(hw)` | `DRadius_DA(double hw)` | 数值差分 dR/dA |
| `dA_dh(hw)` | `DArea_Dh(double hw)` | 数值差分 dA/dh |
| `z_at(x)` | `ZAt(double x)` | 线性插值查询床底高程 |
| `friction_slope(h, Q)` | `FrictionSlope(double h, double Q)` （override） | 多子槽摩阻坡度求和 |
| `dSf_dA(h, Q)` | `DFrictionSlope_DA(double h, double Q)` （override） | 多子槽 ∂Sf/∂A |
| `dSf_dQ(h, Q)` | `DFrictionSlope_DQ(double h, double Q)` （override） | 多子槽 ∂Sf/∂Q |

### 6.3 算法说明

- 不规则断面 `Properties` 通过对相邻节点梯形数值积分得到 A 和 P，再计算 R = A/P 和 T（最高水位到两侧交点的宽度）。
- 导数（`dK_dA`、`dR_dA`、`dA_dh`）Python 直接通过 `scipy` 或手工差分；C# 均用 `±dh = 1e-6` 中心差分计算。

---

## 七、CrossSectionInterpolator / interpolate_cross_section

| Python 函数 | C# 静态方法 | 说明 |
|---|---|---|
| `interpolate_cross_section(xs1, xs2, dist1, dist2)` | `CrossSectionInterpolator.Interpolate(xs1, xs2, dist1, dist2)` | 按距离加权插值两断面，返回新断面对象 |

- Python 是模块级函数；C# 重构为静态工具类 `CrossSectionInterpolator`。
- 算法相同：线性距离权重 `w = dist2 / (dist1 + dist2)`，对底宽、边坡、高程等几何参数加权平均。

---

## 八、Hydraulics 工具函数

Python 文件：`hydraulics.py`（模块级函数）  
C# 文件：`Hydraulics.cs`（`public static class Hydraulics`）

| Python 函数 | C# 静态方法 | 公式/说明 |
|---|---|---|
| `conveyance(A, n, R)` | `Conveyance(double A, double n, double R)` | K = A·R^(2/3)/n |
| `dK_dA_(A, n, R, dR_dA)` | `DConveyance_DA(double A, double n, double R, double dR_dA)` | dK/dA = (R^(2/3) + A·⅔·R^(-1/3)·dR_dA)/n |
| `Sf(Q, A, n, R, K)` | `FrictionSlope(double Q, double K)` | Sf = Q\|Q\|/K² |
| `dSf_dA(Q, A, n, R, dR_dA, K, dK_dA)` | `DFrictionSlope_DA(double Q, double K, double dK_dA)` | ∂Sf/∂A = −2·Sf·(dK/dA)/K |
| `dSf_dQ(Q, A, n, R, K)` | `DFrictionSlope_DQ(double Q, double K)` | ∂Sf/∂Q = 2\|Q\|/K² |
| `normal_flow(bed_slope, K)` | `NormalFlow(double bedSlope, double K)` | Qn = K·√S₀（负坡取负号） |
| `dQn_dA(S_0, dK_dA)` | `DNormalFlow_DA(double S0, double dK_dA)` | dQn/dA = dK/dA·√\|S₀\|（负坡取负号） |
| `froude_num(T, A, Q)` | `FroudeNumber(double T, double A, double Q)` | Fr = V/√(g·D)，D = A/T |
| `dFr_dA(T, A, Q)` | `DFroude_DA(double T, double A, double Q)` | ∂Fr/∂A |
| `dFr_dQ(T, A)` | `DFroude_DQ(double T, double A)` | ∂Fr/∂Q |
| `darcey_weisbach_f(n, R)` | `DarcyWeisbachF(double n, double R)` | f = 8g/C²，C = R^(1/6)/n |
| `Sc(h, T, A, Q, n, R, rc)` | `CurvatureSlope(double h, double T, double A, double Q, double n, double R, double rc)` | 弯道二次流修正坡度 |
| `dSc_dA(h, A, Q, n, R, rc, dR_dA, T)` | `DCurvatureSlope_DA(double h, double A, double Q, double n, double R, double rc, double dR_dA, double T)` | ∂Sc/∂A |
| `dSc_dQ(h, T, A, Q, n, R, rc)` | `DCurvatureSlope_DQ(double h, double T, double A, double Q, double n, double R, double rc)` | ∂Sc/∂Q |
| （无，Python 使用 `scipy.optimize.brentq`） | `Brentq(Func<double,double> f, double a, double b, double tol, int maxIter)` | C# 自实现 Brent 求根 |
| （无，Python 使用 `numpy.interp`） | `Interp(double x, double[] xs, double[] ys)` | C# 自实现线性插值 |
| （无，Python 使用 `numpy.gradient`） | `Gradient(double[] y, double[] x)` | C# 自实现梯度（有限差分） |
| （无，Python 使用 `numpy.trapezoid`） | `Trapezoid(double[] y, double[] x)` | C# 自实现梯形积分 |
| `compute_curv(x_coords, y_coords)`（`utility.py`） | （内联至 `Channel._calcCurvature()`） | 弧长参数化曲率计算 |
| `seconds_to_hms(seconds)`（`utility.py`） | `SecondsToHms(double seconds)`（`Solver.cs` 私有方法） | 秒数转 h:mm:ss 字符串 |
| `euclidean_norm(vector)`（`utility.py`） | （内联至 `PreissmannSolver`） | 欧氏范数 |
| `manhattan_norm(vector)`（`utility.py`） | （未迁移，未使用） | 曼哈顿范数 |

**重要差异**：Python 中 `g = scipy.constants.g`（9.80665 m/s²），C# 中 `Hydraulics.G = 9.80665`（常量字段）。

---

## 九、Hydrograph 类

### 9.1 字段 / 属性

| Python 属性 | C# 字段 | 类型 | 说明 |
|---|---|---|---|
| `self.table` | `_table` | `np.ndarray` / `double[,]?` | 时间-值二维数组 |
| `self.used_function` | `_function` | 函数引用 | 当前使用的插值或解析函数 |

### 9.2 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(function, table)` | `Hydrograph(Func<double,double>? function, double[,]? table)` | 构造，优先使用函数，否则插值 |
| `interpolate_hydrograph(time)` | `InterpolateHydrograph(double time)` | 线性插值表格 |
| `get_at(time)` | `GetAt(double time)` | 查询 t 时刻的值 |
| `set_table(table)` | `SetTable(double[,] table)` | 设置数据表格 |
| `set_function(func)` | `SetFunction(Func<double,double> func)` | 设置解析函数 |

---

## 十、RatingCurve 类

### 10.1 枚举

| Python（字符串） | C# 枚举 `RatingCurveType` | 说明 |
|---|---|---|
| `'polynomial'` | `Polynomial` | 多项式曲线 |
| `'power'` | `Power` | 幂律曲线 |

### 10.2 字段 / 属性

| Python 属性 | C# 属性 | 说明 |
|---|---|---|
| `self.defined` | `Defined` | 是否已定义 |
| `self.type` | `Type` | 曲线类型 |
| `self.a` | `A` | 系数 a |
| `self.b` | `B` | 系数 b |
| `self.c` | `C` | 系数 c（多项式） |
| `self.stage_shift` | `StageShift` | 水位偏移量 |
| `self.function` | `_function` | 查流量函数（可选） |
| `self.derivative` | `_derivative` | 导数函数（可选） |

### 10.3 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `set(type, a, b, c, stage_shift)` | `Set(RatingCurveType type, double a, double b, double c, double stageShift)` | 手动设置曲线系数 |
| `discharge(stage, time)` | `Discharge(double stage, double time)` | 查询指定水位对应流量 |
| `dQ_dz(stage, time)` | `DQ_Dz(double stage, double time)` | 流量对水位的导数 |
| `stage(discharge, trial_stage, time, tolerance, rate)` | `Stage(double discharge, double trialStage, double tol)` | 反查水位（牛顿迭代） |
| `fit(discharges, stages, stage_shift, type, scale, degree)` | `Fit(double[] discharges, double[] stages, RatingCurveType type, double stageShift)` | 从测量数据拟合曲线 |
| `tostring()` | （无，未迁移） | 返回公式字符串 |

**拟合算法差异**：
- Python 多项式拟合可选 `scale=True`（使用 `numpy.polynomial.Polynomial.fit` 归一化） 或 `scale=False`（使用 `numpy.polyfit`）。
- C# 仅实现无归一化最小二乘拟合（通过 `SolveLinear3x3` 自实现），与 Python `scale=False` 路径等价。

---

## 十一、LumpedStorage 类

### 11.1 字段 / 属性

| Python 属性 | C# 属性/字段 | 说明 |
|---|---|---|
| `self.rating_curve` | `RatingCurve` | 出流水位-流量关系曲线 |
| `self.surface_area` | `SurfaceArea` | 常数水面面积（m²，面积曲线为空时使用） |
| `self.min_stage` | `MinStage` | 最低允许水位（m） |
| `self.stage_hydrograph` | `StageHydrograph` | 库水位时间序列（`list` / `List<double[]>`） |
| `self.area_curve` | `AreaCurve` | 水位-面积关系表（numpy 数组 / `double[,]`） |
| `self.reservoir_length` | `ReservoirLength` | 库区等效长度（m，用于摩阻损失） |
| `self.capture_losses` | `CaptureLosses` | 是否计算能量损失 |
| `self.Cc` | `Cc` | 收缩系数（扩散损失经验系数） |
| `self.K_q` | `KQ` | 经验损失系数 |
| `self.Y_min` | `_yMin` | 水位搜索下限（m） |
| `self.Y_max` | `_yMax` | 水位搜索上限（m） |
| `self.alpha` | `_alpha` | 面积曲线缩放系数 |
| `self.beta` | `_beta` | 面积曲线偏移系数 |

### 11.2 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(solution_boundaries, surface_area, min_stage, rating_curve)` | `LumpedStorage(double yMin, double yMax, double? surfaceArea, double? minStage, RatingCurve? ratingCurve)` | 构造 |
| `mass_balance(duration, vol_in, Y_old, time)` | `MassBalance(double duration, double volIn, double yOld, double time)` | Brent 法求解库水位（质量守恒） |
| `dY_new_dvol_in(duration, vol_in, Y_old, time)` | `DYnew_DvolIn(double duration, double volIn, double yOld, double time)` | d(Y_new)/d(vol_in)（用于 Jacobi 矩阵） |
| `energy_loss(entry_area, flow, roughness, hydraulic_radius, A_str)` | `EnergyLoss(double entryArea, double flow, double roughness, double hydraulicRadius, double? aStr)` | 总能量损失（摩阻 + 扩散 + 经验） |
| `friction_loss(A_ent, Q, n, R)` | `FrictionLoss(double aEnt, double Q, double n, double R)` （私有） | 摩阻水头损失 hf = Sf·L |
| `expansion_loss(A_ent, Q, A_str)` | `ExpansionLoss(double aEnt, double Q, double? aStr)` （私有） | 突扩损失 |
| `empirical_loss(Q, A_ent)` | `EmpiricalLoss(double aEnt, double Q)` （私有） | 经验损失 |
| `dhl_dA(entry_area, flow, roughness, hydraulic_radius, dR_dA, A_str)` | `Dhl_DA(double entryArea, double flow, double roughness, double hydraulicRadius, double dR_dA, double? aStr)` | ∂h_L/∂A |
| `dhl_dQ(entry_area, flow, roughness, hydraulic_radius, A_str)` | `Dhl_DQ(double entryArea, double flow, double roughness, double hydraulicRadius, double? aStr)` | ∂h_L/∂Q |
| `set_area_curve(table, alpha, beta, update_solution_boundaries)` | `SetAreaCurve(double[,] table, double alpha, double beta, bool updateBoundaries)` | 设置水位-面积关系表 |
| `area_at(stage)` | `AreaAt(double stage)` | 按水位查插值面积 |
| `net_vol_change(Y1, Y2)` | `NetVolChange(double y1, double y2)` | 两水位间净容积差 |
| `dA_dY(stage)` | `DAdy(double stage)` | dA/dY（面积对水位的导数） |

---

## 十二、Channel 类

### 12.1 初始化方法枚举

| Python 字符串 | C# 枚举 `InitializationMethod` | 说明 |
|---|---|---|
| `'linear'` | `Linear` | 线性插值初始水深 |
| `'GVF_equation'` | `GVFEquation` | 渐变流方程（从下游向上游积分） |
| `'steady-state'` | `SteadyState` | 均匀流正常水深 |

### 12.2 字段 / 属性

| Python 属性 | C# 属性/字段 | 说明 |
|---|---|---|
| `self.upstream_boundary` | `UpstreamBoundary` | 上游边界 |
| `self.downstream_boundary` | `DownstreamBoundary` | 下游边界 |
| `self.initial_flow_rate` | `InitialFlowRate` | 初始流量 |
| `self.roughness` | `Roughness` | 统一糙率（可选） |
| `self.width` | `Width` | 河道宽度（可选） |
| `self.length` | `Length` | 河道总长度 |
| `self.interpolation_method` | `InitMethod` | 初始条件计算方法 |
| `self.initial_conditions` | `InitialConditions` | 初始条件 [nNodes, 2]（h, Q） |
| `self.conditions_initialized` | `ConditionsInitialized` | 是否已完成初始化 |
| `self.xs_at_node` | `XsAtNode` | 各节点水力断面数组 |
| `self.ch_at_node` | `ChAtNode` | 各节点桩号数组 |
| `self.xs_chainages` | `_xsChainages` （私有） | 用户输入断面桩号 |
| `self.input_xs` | `_inputXs` （私有） | 用户输入断面数组 |
| `self.coords` | `_coords` （私有） | 河道平面坐标 [n, 2] |
| `self.coords_chainages` | `_coordsChainages` （私有） | 坐标对应桩号 |

### 12.3 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(upstream_boundary, downstream_boundary, initial_flow, roughness, width, interpolation_method)` | `Channel(Boundary upstreamBoundary, Boundary downstreamBoundary, double initialFlow, ...)` | 构造 |
| `set_cross_sections(chainages, sections)` | `SetCrossSection(double[] chainages, CrossSection[] sections)` | 注册用户输入断面 |
| `set_coords(coords, chainages)` | `SetCoords(double[,] coords, double[] chainages)` | 设置平面坐标（弯道曲率计算用） |
| `initialize_conditions(n_nodes)` | `InitializeConditions(int nNodes)` | 计算初始条件并分配节点断面 |
| `Se(h, Q, i)` | `Se(double h, double Q, int i)` | 能量坡度（Sf + Sc） |
| `dSe_dA(h, Q, i)` | `DSe_DA(double h, double Q, int i)` | ∂Se/∂A |
| `dSe_dQ(h, Q, i)` | `DSe_DQ(double h, double Q, int i)` | ∂Se/∂Q |
| `area_at(i, hw)` | `AreaAt(int i, double hw)` | 节点 i 处过水面积 |
| `hydraulic_radius(i, hw)` | `HydraulicRadius(int i, double hw)` | 节点 i 处水力半径 |
| `top_width(i, hw)` | `TopWidth(int i, double hw)` | 节点 i 处水面宽 |
| `bed_level_at(i)` | `BedLevelAt(int i)` | 节点 i 处床底高程 |
| `dA_dh(i, hw)` | `DArea_Dh(int i, double hw)` | 节点 i 处 dA/dh |
| `_initialize_geometry(n_nodes)` | `_initializeGeometry(int nNodes)` （私有） | 分配节点断面（内部调用） |
| `_create_provisional_cross_sections()` | `_createProvisionalCrossSections()` （私有） | 生成默认梯形断面 |
| `_calc_curvature()` | `_calcCurvature()` （私有） | 弯道曲率计算 |
| `_interpolate_cross_sections()` | `_interpolateCrossSections()` （私有） | 按桩号插值中间断面 |
| `_linear_conditions(n_nodes, Q)` | `_linearConditions(int nNodes, double Q)` （私有） | 线性初始水深 |
| `_gvh_conditions(n_nodes, Q)` | `_gvfConditions(int nNodes, double Q)` （私有） | 渐变流初始水深 |
| `_steady_conditions(n_nodes, Q)` | `_steadyConditions(int nNodes, double Q)` （私有） | 均匀流初始水深 |
| `get_n(A, i)` | （内联至 `CrossSection.GetEquivalentN`） | 等效糙率 |
| （无，使用 `numpy.linspace`） | `Linspace(double start, double end, int n)` | C# 自实现均匀间距 |

---

## 十三、Solver 基类

### 13.1 字段 / 属性

| Python 属性 | C# 属性 | 说明 |
|---|---|---|
| `self.channel` | `Channel` | 关联的 Channel 对象 |
| `self.time_step` | `TimeStep` | 时间步长（s） |
| `self.spatial_step` | `SpatialStep` | 空间步长（m） |
| `self.time_level` | `TimeLevel` | 当前时间层序号 |
| `self.number_of_nodes` | `NumberOfNodes` | 空间节点数 |
| `self.number_of_time_levels` | `NumberOfTimeLevels` | 总时间层数 |
| `self.num_celerity` | `NumCelerity` | 数值波速 = Δx/Δt |
| `self._solved` | `Solved` | 是否已完成计算 |
| `self.flow` | `Flow` | 流量数组 [nK, nX] |
| `self.depth` | `Depth` | 水深数组 [nK, nX] |
| `self.bed_profile` | `BedProfile` | 床底高程数组 [nX] |
| `self.level` | `Level` | 水位数组 [nK, nX] |
| `self.area` | `Area` | 过水面积数组 [nK, nX] |
| `self.top_width` | `TopWidth` | 水面宽数组 [nK, nX] |
| `self.froude_number` | `FroudeNumber` | 弗劳德数数组 [nK, nX] |
| `self.velocity` | `Velocity` | 流速数组 [nK, nX] |
| `self.wave_celerity` | `WaveCelerity` | 波速数组 [nK, nX] |
| `self.amplitude` | `Amplitude` | 洪峰增量数组 [nK, nX] |
| `self.peak_amplitude` | `PeakAmplitude` | 峰值增量数组 [nX] |
| `self.total_sim_duration` | `TotalSimDuration` | 总仿真时长（s） |
| `self.storage_stage` | `StorageStage` | 集总库水位时序 [nK] |
| `self.storage_outflow` | `StorageOutflow` | 集总库出流时序 [nK] |
| `self.regularization` | （未迁移） | Python 特有：正则化选项 |
| `self.eps` | （未迁移） | Python 特有：正则化参数 |

### 13.2 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(channel, time_step, spatial_step, simulation_time, regularization, fit_spatial_step)` | `Solver(Channel channel, double timeStep, double spatialStep, double simulationTime, bool fitSpatialStep)` | 构造 |
| `fit_spatial_step()` | `FitSpatialStep()` | 调整空间步长以整除河道总长 |
| `run(verbose)` | `Run(int verbose)` （抽象） | 运行仿真（子类实现） |
| `initialize_t0()` | `InitializeT0()` | 从初始条件填充 t=0 层 |
| `prepare_results()` | `PrepareResults()` | 后处理：计算 A、T、Fr、V 等 |
| `save_results(folder_path, file_name)` | `SaveResults(string folderPath, string? fileName)` | 输出 Excel 结果文件 + 文本摘要 |
| `_finalize(verbose)` | `Finalize(int verbose)` （保护） | 收尾：调用 `PrepareResults` |
| `depth_at(k, i)` | `DepthAt(int k, int i)` | 读取 [k, i] 水深 |
| `flow_at(k, i, chi_scaling)` | `FlowAt(int k, int i)` | 读取 [k, i] 流量 |
| `area_at(k, i, regularization)` | `AreaAt(int k, int i)` | 读取 [k, i] 过水面积 |
| `water_level_at(k, i)` | `WaterLevelAt(int k, int i)` | 水位 = BedLevel + Depth |
| `Se_at(k, i)` | `SeAt(int k, int i)` | 能量坡度（k, i 节点） |
| `dA_dh(k, i)` | `DADhAt(int k, int i)` | dA/dh（k, i 节点） |
| `A_reg(A)` | （未迁移） | Python 特有：正则化面积 |
| `Q_eff(Q, A_reg)` | （未迁移） | Python 特有：有效流量 |

**输出格式差异**：
- Python 使用 `pandas.ExcelWriter` + `openpyxl` 输出 Excel；C# 使用 `ClosedXML` 库（`XLWorkbook`）。
- Python 文本摘要用 `f-string`；C# 用 `StreamWriter`，逻辑等价。

---

## 十四、PreissmannSolver 类

### 14.1 字段 / 属性

| Python 属性 | C# 属性/字段 | 说明 |
|---|---|---|
| `self.theta` | `Theta` | Preissmann 加权因子 θ（建议 ≥ 0.5） |
| `self.J` | `（内嵌为局部变量）` | 雅可比矩阵（Python 为 `scipy.sparse.csr_matrix`；C# 为 `double[,]` 稠密矩阵） |
| `self.R` | `_R` | 残差向量 |
| `self.unknowns` | `_unknowns` | 未知量向量（交替排列 h₀,Q₀,h₁,Q₁,...） |

### 14.2 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(theta, **kwargs)` | `PreissmannSolver(Channel channel, double theta, ...)` | 构造 |
| `initialize_t0()` | （在构造函数中调用基类 `InitializeT0`） | 初始化 t=0 层 |
| `run(tolerance, verbose, max_iter, diagnos)` | `Run(int verbose)` | 牛顿迭代推进仿真 |
| `update_guesses()` | （内联于 `Run`） | 将 `_unknowns` 写回 `Depth`/`Flow` |
| `compute_residual_vector()` | `ComputeResidualVector()` （私有） | 计算残差向量 R |
| `compute_jacobian()` | `ComputeJacobian()` → 返回 `double[,]` （私有） | 构造雅可比矩阵 |
| `upstream_residual()` | `UpstreamResidual()` （私有） | 上游边界残差 |
| `downstream_residual()` | `DownstreamResidual()` （私有） | 下游边界残差 |
| `continuity_residual(i)` | `ContinuityResidual(int i)` （私有） | 节点 i 连续方程残差 |
| `momentum_residual(i)` | `MomentumResidual(int i)` （私有） | 节点 i 动量方程残差 |
| `check_criticality()` | `CheckCriticality()` （私有） | 打印发散时各节点弗劳德数 |
| `time_diff(...)` | `TimeDiff(...)` （私有） | 时间差分：(k1 - k0) / Δt |
| `spatial_diff(...)` | `SpatialDiff(...)` （私有） | 空间差分：(i+1 - i) / Δx（θ 加权） |
| `cell_avg(...)` | `CellAvg(...)` （私有） | 时空均值（θ 加权） |
| （无，Python 使用 `scipy.sparse.linalg.spsolve`） | （使用 MathNet.Numerics 或自实现 Gauss 消元） | 线性方程组 Ax=b 求解 |

**稀疏 vs 稠密矩阵**：
- Python 使用 `scipy.sparse.csr_matrix` 避免大规模内存占用，适合节点数多时。
- C# 使用稠密 `double[,]` 矩阵（`MathNet.Numerics` 求解），对中等规模计算足够用。

---

## 十五、LaxSolver 类

### 15.1 枚举

| Python 字符串 | C# 枚举 `LaxSecondaryBC` | 说明 |
|---|---|---|
| `'constant'` | `Constant` | 常数延伸（使用边界值） |
| `'mirror'` | `Mirror` | 镜像延伸 |
| `'linear'` | `Linear` | 线性外推 |

### 15.2 字段 / 属性

| Python 属性 | C# 属性 | 说明 |
|---|---|---|
| `self.secondary_BC[0]` | `UpstreamSecondaryBC` | 上游辅助边界类型 |
| `self.secondary_BC[1]` | `DownstreamSecondaryBC` | 下游辅助边界类型 |

### 15.3 方法

| Python 方法 | C# 方法 | 说明 |
|---|---|---|
| `__init__(secondary_BC, **kwargs)` | `LaxSolver(Channel channel, ..., LaxSecondaryBC upBC, LaxSecondaryBC downBC)` | 构造 |
| `initialize_t0()` | （基类 `InitializeT0()`） | 用面积代替水深存储 t=0 |
| `run(verbose)` | `Run(int verbose)` | 显式 Lax-Friedrichs 推进 |
| `us_ghost_node()` | `UsGhostNode()` （私有） | 上游虚拟节点 |
| `ds_ghost_node()` | `DsGhostNode()` （私有） | 下游虚拟节点 |
| `compute_node(i)` | `ComputeNode(int i)` （私有） | 计算节点 i（分派到上/中/下） |
| `compute_upstream_node()` | `ComputeUpstreamNode()` （私有） | 上游节点 Lax 格式 |
| `compute_downstream_node()` | `ComputeDownstreamNode()` （私有） | 下游节点 Lax 格式 |
| `new_area(A_im1, A_ip1, Q_im1, Q_ip1)` | `NewArea(double A_im1, double A_ip1, double Q_im1, double Q_ip1)` （私有） | 连续方程 Lax 离散 |
| `new_flow(...)` | `NewFlow(...)` （私有） | 动量方程 Lax 离散 |
| `check_cfl_all()` | `CheckCflAll()` （私有） | 全节点 CFL 条件检查 |
| `check_cfl_condition(velocity, depth)` | （内联至 `CheckCflAll`） | 单节点 CFL 检查 |
| `spatial_diff(ip1, im1)` | `LaxSpatialDiff(double ip1, double im1)` （私有） | 0.5(ip1−im1)/Δx |
| `cell_avg(ip1, im1)` | `LaxCellAvg(double ip1, double im1)` （私有） | 0.5(ip1+im1) |

**变量存储差异**：
- Python 的 Lax 求解器沿用 `Solver` 基类的 `self.area[k, i]` 存储水面面积，再由 `area_at()` 查询；
  C# 的 `LaxSolver` 通过 `AreaToDepth(nodeIdx, A)` 将面积转换为水深后存入 `Depth` 数组，保持与 Preissmann 求解器一致。

---

## 十六、算法差异说明

| 差异点 | Python 实现 | C# 实现 |
|---|---|---|
| 线性代数库 | `numpy`（数组运算）、`scipy.sparse`（稀疏矩阵）、`scipy.sparse.linalg.spsolve`（线性方程组） | `MathNet.Numerics`（矩阵运算与求解）或自实现 |
| 根求解器 | `scipy.optimize.brentq` | `Hydraulics.Brentq()`（自实现 Brent 方法） |
| 插值函数 | `numpy.interp` | `Hydraulics.Interp()`（自实现线性插值） |
| 梯度计算 | `numpy.gradient` | `Hydraulics.Gradient()`（自实现中心差分） |
| 积分 | `numpy.trapezoid` | `Hydraulics.Trapezoid()`（自实现梯形积分） |
| 常数 g | `scipy.constants.g`（9.80665） | `Hydraulics.G = 9.80665`（常量字段） |
| Jacobi 矩阵 | 稀疏矩阵（`scipy.sparse.coo_matrix → csr_matrix`） | 稠密矩阵（`double[,]`） |
| Excel 输出 | `pandas` + `openpyxl` | `ClosedXML`（`XLWorkbook`） |
| 类型系统 | 动态类型（鸭子类型） | 强类型（泛型 + 抽象类 + 接口） |
| 枚举定义 | 字符串字面量 | `enum` 类型（编译期安全） |
| 正则化选项 | 有（`regularization`、`eps`、`A_reg`、`Q_eff`） | 未迁移 |
| 性能缓存 | 无 | CrossSection 基类缓存上一次 `Properties(hw)` 结果 |
| 弯道曲率 | `utility.compute_curv()`（弧长参数化） | `Channel._calcCurvature()`（三点角度法）|
