using System;
using System.Globalization;
using System.IO;

namespace FlowSim.Models
{
    /// <summary>
    /// GERD（大埃塞俄比亚复兴大坝）水库出流过程线。
    /// <para>
    /// 基于蓄量（V-Z）曲线与出流能力公式，采用 Brent 法对每个时步
    /// 求解质量守恒方程，计算逐时步出流量，最终构建出流过程线表格。
    /// </para>
    /// <para>
    /// 出流能力由四类泄水结构的公式之和给出：
    /// <list type="bullet">
    ///   <item>闸控溢流堰（gated spillway）</item>
    ///   <item>台阶式溢洪道（stepped spillway）</item>
    ///   <item>应急溢洪道（emergency spillway）</item>
    ///   <item>水力发电机组（turbines，固定基流 1562.5 m³/s）</item>
    /// </list>
    /// </para>
    /// </summary>
    public class GerdHydrograph : Hydrograph
    {
        // ── 常量 ──
        /// <summary>水轮机固定基流（m³/s）。</summary>
        public const double TurbineFlow = 1562.5;

        private const double SpillawayCrest  = 624.9;   // 闸控溢流堰堰顶高程（m）
        private const double MaxOpLevel      = 640.0;   // 最大操作水位（m）
        private const double SteppedCrest    = 640.0;   // 台阶溢洪道堰顶（m）
        private const double EmergencyCrest  = 642.0;   // 应急溢洪道堰顶（m）
        private const double BrentLo         = 624.9;   // Brent 搜索下界（m）
        private const double BrentHi         = 645.0;   // Brent 搜索上界（m）

        // ── V-Z 曲线 ──
        private double[]? _stages;   // 蓄量对应水位（m）
        private double[]? _vols;     // 蓄量（10⁶ m³）

        // ── 对外只读属性 ──
        /// <summary>V-Z 曲线中的水位数组（加载后可读）。</summary>
        public double[]? Stages => _stages;

        // ====================================================================
        // 公共方法
        // ====================================================================

        /// <summary>
        /// 构建 GERD 出流过程线。
        /// </summary>
        /// <param name="inflowHydrograph">上游入库过程线（<see cref="Hydrograph"/>）。</param>
        /// <param name="timeStep">时间步长（s）。</param>
        /// <param name="duration">总模拟时长（s）。</param>
        /// <param name="initialStage">GERD 初始蓄水位（m）。</param>
        /// <param name="volCurvePath">V-Z 曲线 CSV 文件路径（两列：蓄量(10⁶m³), 水位(m)）。</param>
        public void Build(Hydrograph inflowHydrograph, double timeStep, double duration,
                          double initialStage, string volCurvePath)
        {
            LoadVolCurve(volCurvePath);

            int nSteps = (int)(duration / timeStep) + 1;
            var table = new double[nSteps, 2];

            double stage0   = initialStage;
            double inflow0  = inflowHydrograph.GetAt(0);
            double outflow0 = ComputeRelease(inflow0, stage0, initialStage);

            table[0, 0] = 0;
            table[0, 1] = outflow0;

            for (int ti = 1; ti < nSteps; ti++)
            {
                double t       = ti * timeStep;
                double inflow1 = inflowHydrograph.GetAt(t);
                double avgInflow = 0.5 * (inflow1 + inflow0);
                double vol0    = InterpVolume(stage0);

                // 质量守恒方程：ΔV = (Q_in,avg - Q_out,avg) · Δt  →  Brent 求解 stage_1
                double qReq = inflow1;
                double stage1 = Brentq(s =>
                {
                    double outflow1  = ComputeRelease(qReq, s, initialStage);
                    double avgOut    = 0.5 * (outflow1 + outflow0);
                    double vol1      = InterpVolume(s);
                    return (vol1 - vol0) - (avgInflow - avgOut) * timeStep * 1e-6;
                }, BrentLo, BrentHi);

                double outflow1 = ComputeRelease(qReq, stage1, initialStage);

                table[ti, 0] = t;
                table[ti, 1] = outflow1;

                stage0  = stage1;
                inflow0 = inflow1;
                outflow0 = outflow1;
            }

            SetTable(table);
        }

        // ====================================================================
        // 出流能力公式（翻译自 Python gerd_discharge.py）
        // ====================================================================

        /// <summary>GERD 全出流能力（不含底孔，只含溢洪道 + 发电机组）。</summary>
        public double EffectiveCapacity(double wl)
        {
            double q1 = GatedSpillway(wl)  * Alpha(wl);   // 闸控溢洪道（含启用率）
            double q2 = SteppedSpillway(wl);                // 台阶溢洪道
            double q3 = EmergencySpillway(wl);              // 应急溢洪道
            return q1 + q2 + q3 + TurbineFlow;
        }

        // ── 单个泄水结构出流公式 ──
        /// <summary>闸控溢洪道（gated ogee crest spillway）。</summary>
        public static double GatedSpillway(double wl)    => 196.4017 * Math.Pow(Math.Max(0, wl - SpillawayCrest), 1.5);
        /// <summary>台阶式溢洪道（stepped spillway）。</summary>
        public static double SteppedSpillway(double wl)  => 447.3594 * Math.Pow(Math.Max(0, wl - SteppedCrest), 1.5);
        /// <summary>应急溢洪道（emergency spillway）。</summary>
        public static double EmergencySpillway(double wl)=> 654.6723 * Math.Pow(Math.Max(0, wl - EmergencyCrest), 1.5);

        /// <summary>
        /// 闸控溢洪道启用率 α（闸控比例，0=未启用，1=全开）。
        /// 线性从堰顶 624.9 m 升至最大操作水位 640 m。
        /// </summary>
        public static double Alpha(double wl)
        {
            if (wl <= SpillawayCrest) return 0;
            if (wl >= MaxOpLevel)     return 1;
            return (wl - SpillawayCrest) / (MaxOpLevel - SpillawayCrest);
        }

        // ====================================================================
        // 内部辅助方法
        // ====================================================================

        private double ComputeRelease(double inflow, double stage, double initialStage)
        {
            double capacity = EffectiveCapacity(stage);
            double discharge;

            if (stage > initialStage)
                discharge = capacity;
            else
            {
                discharge = Math.Min(inflow, capacity);
                discharge = Math.Max(discharge, TurbineFlow);
            }
            return discharge;
        }

        private void LoadVolCurve(string path)
        {
            var lines = File.ReadAllLines(path);
            var vs = new System.Collections.Generic.List<double>();
            var ss = new System.Collections.Generic.List<double>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double vol))   continue;
                if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double stage)) continue;
                vs.Add(vol);
                ss.Add(stage);
            }

            if (vs.Count < 2)
                throw new InvalidOperationException("GERD V-Z 曲线至少需要 2 个数据点。");

            _vols   = vs.ToArray();
            _stages = ss.ToArray();
        }

        private double InterpVolume(double stage)
        {
            // 对 V-Z 曲线做线性插值（V = f(stage)）
            double[] s = _stages!, v = _vols!;
            if (stage <= s[0]) return v[0];
            if (stage >= s[s.Length - 1]) return v[v.Length - 1];
            for (int i = 0; i < s.Length - 1; i++)
            {
                if (stage <= s[i + 1])
                {
                    double t = (stage - s[i]) / (s[i + 1] - s[i]);
                    return v[i] + t * (v[i + 1] - v[i]);
                }
            }
            return v[v.Length - 1];
        }

        // ====================================================================
        // Brent 方法（无依赖版）
        // ====================================================================

        /// <summary>
        /// Brent 法求方程 f(x)=0 在 [a,b] 内的根。
        /// 要求 f(a) 与 f(b) 异号。
        /// </summary>
        public static double Brentq(Func<double, double> f, double a, double b,
                                    double tol = 1e-8, int maxIter = 150)
        {
            double fa = f(a), fb = f(b);
            if (fa * fb > 0)
                throw new InvalidOperationException(
                    $"Brentq: f({a:G4})={fa:G4} 和 f({b:G4})={fb:G4} 同号，方程无根或根在区间外。");

            double c = a, fc = fa;
            double s = 0, d = 0;
            bool mflag = true;

            // 确保 |f(a)| >= |f(b)|
            if (Math.Abs(fa) < Math.Abs(fb))
            {
                (a, b, fa, fb) = (b, a, fb, fa);
                (c, fc) = (a, fa);
            }

            for (int i = 0; i < maxIter; i++)
            {
                if (Math.Abs(fa - fb) > 1e-15 && Math.Abs(fb - fc) > 1e-15 && Math.Abs(fa - fc) > 1e-15)
                {
                    // 逆二次插值
                    s = a * fb * fc / ((fa - fb) * (fa - fc))
                      + b * fa * fc / ((fb - fa) * (fb - fc))
                      + c * fa * fb / ((fc - fa) * (fc - fb));
                }
                else
                {
                    // 割线法
                    s = b - fb * (b - a) / (fb - fa);
                }

                double ab34 = (3 * a + b) / 4.0;
                bool cond1 = !((ab34 <= s && s <= b) || (b <= s && s <= ab34));
                bool cond2 = mflag  && Math.Abs(s - b) >= Math.Abs(b - c) / 2.0;
                bool cond3 = !mflag && Math.Abs(s - b) >= Math.Abs(c - d) / 2.0;
                bool cond4 = mflag  && Math.Abs(b - c) < tol;
                bool cond5 = !mflag && Math.Abs(c - d) < tol;

                if (cond1 || cond2 || cond3 || cond4 || cond5)
                {
                    s = (a + b) / 2.0;   // 二分
                    mflag = true;
                }
                else
                {
                    mflag = false;
                }

                double fs = f(s);
                d = c; c = b; fc = fb;

                if (fa * fs < 0) { b = s; fb = fs; }
                else             { a = s; fa = fs; }

                if (Math.Abs(fa) < Math.Abs(fb))
                    (a, b, fa, fb) = (b, a, fb, fa);

                if (Math.Abs(b - a) < tol || Math.Abs(fb) < tol)
                    return b;
            }
            return b;
        }
    }
}
