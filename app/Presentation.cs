using System;
namespace RedmiFanControl {
    enum NoticeTone { Information,Success,Warning,Error }
    sealed class ControlNotice {
        public string Title,Detail;public NoticeTone Tone;
        public ControlNotice(string title,string detail,NoticeTone tone){Title=title;Detail=detail;Tone=tone;}
    }
    static class Presentation {
        public static bool IsLive(Frame f){return f!=null && f.Connected && !f.Ended && Files.Fresh(f.Utc,3);}
        public static string FanState(int rpm,double output,bool manual){
            if(rpm==0)return output>0?"等待起转":manual?"按设置停转":"系统停转";
            return output==0?"减速中":"运转中";
        }
        public static ControlNotice Describe(Frame f,Message draft,bool waiting,double waitSeconds){
            if(!IsLive(f))return new ControlNotice("未连接 · 设置尚未应用",(f==null?null:f.Error)??"请点击“连接硬件”。没有有效实时数据时显示 --。",NoticeTone.Warning);
            if(!f.CanControl){
                if(f.Connected && !f.Ended && !f.CpuC.HasValue && f.Error!=null)
                    return new ControlNotice("等待温度传感器恢复",f.Error+" 后台会自动重试；恢复有效温度后即可继续连接。",NoticeTone.Warning);
                if(f.Error!=null)return new ControlNotice(f.AutoConfirmed?"保护已停止调速 · 已恢复系统自动":"保护已停止调速 · 恢复尚未确认",f.Error+" 重新连接后可再次手动应用。",NoticeTone.Error);
                if(f.Manual && !f.Owned)return new ControlNotice("其他程序正在控制 · 仅监测","请先让其他风扇控制程序恢复自动，避免同时接管。",NoticeTone.Warning);
                if(f.CpuC>=ThermalLimits.EmergencyC)return new ControlNotice("温度达到 95°C · 保持系统自动","降温并重新连接后再应用设置。",NoticeTone.Warning);
                return new ControlNotice("正在连接 · 等待温度看护就绪","实时监测已连接；调速按钮将在看护就绪后启用。",NoticeTone.Information);
            }
            if(waiting)return new ControlNotice(waitSeconds>5?"应用尚未确认 · 可重新连接":"正在应用 · 等待硬件确认","当前仍以上次读回的硬件状态为准。",NoticeTone.Information);
            if(!f.Manual || !f.Owned){
                string detail=f.Rpm1==0 && f.Rpm2==0 && f.Output1==0 && f.Output2==0?
                    "系统当前让风扇停转，0 RPM 是有效读数。下方设置尚未接管；点击保存并应用后生效。":
                    "系统负责调速。选择控制方式后，点击保存并应用；重启后也需手动应用。";
                return new ControlNotice("系统自动 · 当前编辑尚未应用",f.Error??detail,NoticeTone.Information);
            }
            bool same=draft!=null && f.ActiveSettingsKey==ControlPolicy.SettingsKey(draft);
            string title="已生效："+(f.ControlMode=="CURVE"?"温度曲线":f.ControlMode=="RPM"?"目标转速":"固定百分比")+" · 请求 "+f.RequestedOutputPercent+"% → 目标 "+f.SetPercent+"%";
            if(!same)title+=" · 编辑未应用";
            string reason=f.SafetyStatus??f.DecisionReason??(f.SetPercent==0?"按设置请求停转中，风扇可能需要短暂滑行。":"正在按设置调速。");
            if(f.RuleCode!=null)title+=" · "+ControlPolicy.RuleName(f.RuleCode);
            return new ControlNotice(title,reason+(same?" 当前设置仍生效，无需重连。":" 当前仍运行上次设置，编辑后需再次应用。"),same?NoticeTone.Success:NoticeTone.Warning);
        }
        public static bool ExitConfirmed(Frame frame,string session){
            return frame!=null && frame.Session==session && frame.Ended && frame.AutoConfirmed && !frame.Manual && Files.Fresh(frame.Utc,30);
        }
    }
}
