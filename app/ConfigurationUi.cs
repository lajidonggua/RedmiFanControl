using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        CheckBox allowStop;NumericUpDown stopBelow,minimumRun;
        Button editCurve,saveConfig;Label curveSummary,configStatus;string savedConfiguration;
        void InitializeStopRules(Control parent){
            allowStop=new CheckBox{Text="允许低温停转",Checked=true,Location=new Point(18,42),Size=new Size(270,24)};parent.Controls.Add(allowStop);
            AddLabel(parent,"停转温度上限",18,80,128,24,9,Muted,false);
            stopBelow=new NumericUpDown{Minimum=35,Maximum=60,Value=50,Location=new Point(158,76),Size=new Size(74,27)};parent.Controls.Add(stopBelow);AddLabel(parent,"°C",242,80,40,24,9,Muted,false);
            AddLabel(parent,"最低运行输出",18,119,128,24,9,Muted,false);
            minimumRun=new NumericUpDown{Minimum=10,Maximum=40,Value=20,Location=new Point(158,115),Size=new Size(74,27)};parent.Controls.Add(minimumRun);AddLabel(parent,"%",242,119,40,24,9,Muted,false);
            AddLabel(parent,"60°C 起至少 30% · 80°C 满速\n95°C 恢复系统自动\n这些规则仅约束本程序的手动调速。",18,162,270,66,8,Muted,false);
            hints.SetToolTip(stopBelow,"低于此温度允许停转；达到时至少按最低运行输出运行。无额外 5°C 回差。边界附近可能反复启停，请合理设置节点。");
            hints.SetToolTip(minimumRun,"请求低于此输出时：允许停转且温度低于上限则执行 0%；否则执行此最低输出。");
            allowStop.CheckedChanged+=delegate{RefreshActions();};stopBelow.ValueChanged+=delegate{RefreshActions();};minimumRun.ValueChanged+=delegate{RefreshActions();};
        }
        void InitializeConfigurationUi(Control parent){
            editCurve=Button(parent,"编辑曲线…",340,4,174,32,false);
            curveSummary=AddLabel(parent,"",340,44,174,70,8,Muted,false);
            editCurve.Click+=delegate{
                CurvePoint[] points;try{points=ParseCurve(curveText.Text);ControlPolicy.ValidateCurve(points);}catch{points=new UserConfiguration().Curve;}
                using(var editor=new CurveEditor(points)){if(editor.ShowDialog(this)==DialogResult.OK)curveText.Text=FormatCurve(editor.Result);}
            };
        }
        static string FormatCurve(CurvePoint[] points){return CurveFormat.Format(points);}
        UserConfiguration ReadConfigurationUi(){
            return new UserConfiguration{Mode=controlMode.SelectedIndex,Percent=(int)number.Value,Rpm=(int)rpmTarget.Value,Curve=ParseCurve(curveText.Text),AllowFanStop=allowStop.Checked,StopBelowC=(int)stopBelow.Value,MinimumRunningPercent=(int)minimumRun.Value};
        }
        string ConfigurationSignature(){return controlMode.SelectedIndex+"|"+number.Value+"|"+rpmTarget.Value+"|"+allowStop.Checked+"|"+stopBelow.Value+"|"+minimumRun.Value+"|"+curveText.Text;}
        void LoadConfigurationUi(){
            string message="预览默认设置，未读写配置";
            var value=preview?new UserConfiguration():ConfigurationStore.LoadDefault(out message);
            controlMode.SelectedIndex=value.Mode;number.Value=value.Percent;rpmTarget.Value=value.Rpm;
            allowStop.Checked=value.AllowFanStop;stopBelow.Value=value.StopBelowC;minimumRun.Value=value.MinimumRunningPercent;curveText.Text=FormatCurve(value.Curve);
            savedConfiguration=!preview && (message.StartsWith("已加载配置") || message.StartsWith("已迁移") || message.Contains("已加载上次备份"))?ConfigurationSignature():null;
            configStatus.Text=message;hints.SetToolTip(configStatus,message+"\n"+ConfigurationStore.DefaultPath);
        }
        void RefreshConfigurationUi(){
            if(curveSummary==null)return;
            try{
                var points=ParseCurve(curveText.Text);ControlPolicy.ValidateCurve(points);
                var summary=new StringBuilder();foreach(var p in points){if(summary.Length>0)summary.Append("  ·  ");summary.Append(p.Temperature).Append("° → ").Append(p.Percent).Append('%');}
                curveSummary.Text=summary.ToString();hints.SetToolTip(curveSummary,curveSummary.Text);
                curvePreview.Points=points;curvePreview.Invalidate();
            }catch{curveSummary.Text="请编辑有效的曲线节点。";curvePreview.Points=null;curvePreview.Invalidate();}
            if(savedConfiguration!=null)configStatus.Text=ConfigurationSignature()==savedConfiguration?"✓ 配置已保存\n是否生效请看上方运行状态":"● 配置有修改，尚未保存\n当前风扇仍按上次模式运行";
            stopBelow.Enabled=allowStop.Checked && !closing;
        }
        bool SaveConfigurationUi(){
            if(preview)return true;
            try{var value=ReadConfigurationUi();ConfigurationStore.Save(ConfigurationStore.DefaultPath,value);savedConfiguration=ConfigurationSignature();configStatus.Text="✓ 配置已保存 · 保存不等于应用";return true;}
            catch(Exception ex){configStatus.Text="配置保存失败";hints.SetToolTip(configStatus,ex.Message);MessageBox.Show(this,"配置未保存："+ex.Message,"保存配置",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
        }
    }
}
