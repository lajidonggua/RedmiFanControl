using System;
namespace RedmiFanControl {
    static class ThermalLimits {
        public const int EmergencyC=95;
        public const int StopResumeC=50,LowOutputC=60,FullSpeedC=80;
        // One stateless rule, shared by the worker and independent guardian.
        public static int MinimumOutput(double temperature,int stopBelowC,int minimumRunning){
            if(temperature>=FullSpeedC)return 100;
            if(temperature>=LowOutputC)return Math.Max(30,minimumRunning);
            if(temperature>=stopBelowC)return minimumRunning;
            return 0;
        }
    }
    public sealed class CurvePoint { public int Temperature; public int Percent; }
    // Pure policy: no clock, files or native I/O. Caller supplies monotonic seconds.
    sealed class ControlPolicy {
        public static string SettingsKey(Message m){
            string rules="|"+m.AllowFanStop+":"+m.StopBelowC+":"+m.MinimumRunningPercent;
            if(m.Command=="SET")return "SET:"+m.Percent+rules;
            if(m.Command=="RPM")return "RPM:"+m.Rpm+rules;
            if(m.Command=="CURVE"){
                ValidateCurve(m.Curve);string text="CURVE:";
                foreach(var p in m.Curve)text+=p.Temperature+":"+p.Percent+";";
                return text+rules;
            }
            return "AUTO";
        }
        public string Mode; public int RequestedRpm; public int RequestedPercent;
        public bool AllowFanStop;public int StopBelowC,MinimumRunningPercent;
        CurvePoint[] points; double lastStep,spinSince=-1,stalledSince=-1;
        int duty=60;bool spinPrepared;
        public string SafetyStatus;
        public int RequestedOutputPercent;public string DecisionReason,RuleCode;
        public static string RuleName(string code){
            switch(code){case "STOP":return "允许停转";case "RUN_MIN":return "最低运行输出";case "THERMAL_MIN":return "60°C 输出下限";case "HOT_FULL":return "80°C 满速";case "SPINUP":return "启动助推";default:return "按请求执行";}
        }
        public static void ValidateCurve(CurvePoint[] p){
            if(p==null || p.Length<2 || p.Length>12)throw new ArgumentException("曲线需要 2–12 个节点。");
            for(int i=0;i<p.Length;i++){
                if(p[i]==null || p[i].Temperature<20 || p[i].Temperature>84 || p[i].Percent<0 || p[i].Percent>100 ||
                    (i>0 && (p[i].Temperature<=p[i-1].Temperature || p[i].Percent<p[i-1].Percent)))
                    throw new ArgumentException("温度须为 20–84°C 且递增，输出须为 0–100% 且不递减。");
            }
            if(p[p.Length-1].Temperature>80 || p[p.Length-1].Percent!=100)throw new ArgumentException("末节点须在 80°C 或以下达到 100%。");
        }
        public static int Interpolate(CurvePoint[] p,double t){
            if(t<=p[0].Temperature)return p[0].Percent;
            for(int i=1;i<p.Length;i++)if(t<=p[i].Temperature)
                return (int)Math.Round(p[i-1].Percent+(p[i].Percent-p[i-1].Percent)*(t-p[i-1].Temperature)/(p[i].Temperature-p[i-1].Temperature));
            return p[p.Length-1].Percent;
        }
        public ControlPolicy(Message m,double now){
            if(m.StopBelowC<35 || m.StopBelowC>60 || m.MinimumRunningPercent<10 || m.MinimumRunningPercent>40)throw new ArgumentException("停转上限须为 35–60°C，非零最低输出须为 10–40%。");
            AllowFanStop=m.AllowFanStop;StopBelowC=m.StopBelowC;MinimumRunningPercent=m.MinimumRunningPercent;
            Mode=m.Command;lastStep=now;
            if(Mode=="SET"){
                if(m.Percent<0 || m.Percent>100)throw new ArgumentException("百分比范围为 0–100。");RequestedPercent=m.Percent;
            }else if(Mode=="RPM"){
                if(m.Rpm!=0 && (m.Rpm<1000 || m.Rpm>6000))throw new ArgumentException("目标范围为 1000–6000 RPM，或 0（停转）。");RequestedRpm=m.Rpm;
            }else if(Mode=="CURVE"){ValidateCurve(m.Curve);points=(CurvePoint[])m.Curve.Clone();}
            else throw new ArgumentException("未知调速模式。");
        }
        public int Next(double now,double temperature,int rpm1,int rpm2){
            if(double.IsNaN(temperature) || double.IsInfinity(temperature) || temperature<5 || temperature>=ThermalLimits.EmergencyC)throw new InvalidOperationException("温度无效或达到 95°C，恢复自动。");
            if(rpm1<0 || rpm2<0 || rpm1>7500 || rpm2>7500)throw new InvalidOperationException("转速无效，恢复自动。");
            int request=RequestedPercent;
            // Use the sampled temperature shown to the user, never a hidden
            // cooling accumulator that can indefinitely disagree with the UI.
            if(Mode=="CURVE")request=Interpolate(points,temperature);
            if(Mode=="RPM"){
                if(RequestedRpm==0)request=0;
                else {
                    int measured=Math.Min(rpm1,rpm2),error=RequestedRpm-measured;
                    if(now-lastStep>=1){
                        if(Math.Abs(error)>120)duty=Math.Max(1,Math.Min(100,duty+(error>0?2:-2)));
                        lastStep=now;
                    }
                    request=duty;
                }
            }
            RequestedOutputPercent=request;SafetyStatus=null;RuleCode="REQUEST";
            DecisionReason="按当前温度/模式的请求输出执行。";
            int floor=ThermalLimits.MinimumOutput(temperature,StopBelowC,MinimumRunningPercent);
            if(request<MinimumRunningPercent){
                bool canStop=AllowFanStop && (Mode!="RPM" || RequestedRpm==0) && floor==0;
                request=canStop?0:MinimumRunningPercent;
                RuleCode=canStop?"STOP":"RUN_MIN";
                DecisionReason=canStop?"请求低于最低运行输出 "+MinimumRunningPercent+"%，且温度低于停转上限 "+StopBelowC+"°C，执行停转。":
                    !AllowFanStop?"未勾选允许停转，执行最低运行输出 "+MinimumRunningPercent+"%。":
                    Mode=="RPM" && RequestedRpm>0?"正 RPM 目标需要持续运行，最低输出 "+MinimumRunningPercent+"%。":
                    "达到停转上限 "+StopBelowC+"°C，执行最低运行输出 "+MinimumRunningPercent+"%；低于 "+StopBelowC+"°C 后立即重算，无额外回差。";
            }
            if(request<floor){
                request=floor;RuleCode=temperature>=ThermalLimits.FullSpeedC?"HOT_FULL":"THERMAL_MIN";
                DecisionReason=temperature>=ThermalLimits.FullSpeedC?"当前温度 ≥80°C，执行满速；低于 80°C 后立即按曲线和停转规则重算。":
                    "当前温度 ≥60°C，最低执行 "+floor+"%；低于 60°C 后立即按曲线和停转规则重算。";
                SafetyStatus=DecisionReason;
            }
            if(request==0){
                spinSince=-1;spinPrepared=false;stalledSince=-1;
                return 0;
            }
            // Start/restart a stopped fan with bounded full duty before using a low target.
            if(!spinPrepared){spinPrepared=true;spinSince=now;if(Math.Min(rpm1,rpm2)>0)spinSince=now-3;}
            if(now-spinSince<3){RuleCode="SPINUP";DecisionReason=SafetyStatus="启动助推：100% 至多 3 秒；若重新满足停转条件，立即取消助推并停转。";return 100;}
            if(Math.Min(rpm1,rpm2)==0){
                if(stalledSince<0)stalledSince=now;
                if(now-stalledSince>=8)throw new InvalidOperationException("非零运行请求下，任一风扇连续 8 秒为 0 RPM，恢复自动；请提高最低运行输出。");
            }else stalledSince=-1;
            return request;
        }
    }
    // Independent guardian checks actual owned target, not UI mode/request.
    sealed class LowSpeedGuard {
        double stalled=-1,unsafeSince=-1;
        public string Check(double now,Ownership owned,Frame f){
            if(owned==null || !owned.Active){stalled=unsafeSince=-1;return null;}
            if(f==null || !f.CpuC.HasValue || double.IsNaN(f.CpuC.Value) || double.IsInfinity(f.CpuC.Value))return "独立温度不可用，恢复自动。";
            if(f.CpuC>=ThermalLimits.EmergencyC)return "CPU 温度达到 95°C，独立看护恢复自动。";
            bool zero=owned.NewRaw==0 || owned.OldRaw==0;
            if(owned.StopBelowC<35 || owned.StopBelowC>60 || owned.MinimumRunningPercent<10 || owned.MinimumRunningPercent>40)return "停转/最低运行配置无效，独立看护恢复自动。";
            int floor=ThermalLimits.MinimumOutput(f.CpuC.Value,owned.StopBelowC,owned.MinimumRunningPercent);
            int required=EcResearch.StrictSuperIoProbe.PercentToRaw(floor);
            bool unsafeOutput=owned.NewRaw<required || (owned.OldRaw>=0 && owned.OldRaw<required);
            if(unsafeOutput){
                if(unsafeSince<0)unsafeSince=now;
                if(now-unsafeSince>=3)return "后台未在 3 秒内完成温度补风，独立看护恢复自动。";
            }else unsafeSince=-1;
            if(!zero && (f.Rpm1==0 || f.Rpm2==0)){
                if(stalled<0)stalled=now;
                if(now-stalled>=12)return "风扇启动/运行转速过低，独立看护恢复自动。";
            }else stalled=-1;
            return null;
        }
    }
}
