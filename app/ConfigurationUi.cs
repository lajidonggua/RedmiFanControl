using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        CheckBox allowStop;NumericUpDown stopBelow,minimumRun;
        Button editCurve,saveConfig;Label curveSummary,configStatus;string savedConfiguration;
        void InitializeStopRules(Control parent){
            allowStop=new CheckBox{Text="允许停转（极低输出按 0% 处理）",Checked=true,Location=new Point(568,32),Size=new Size(350,24)};parent.Controls.Add(allowStop);
            AddLabel(parent,"停转上限（即刻）",568,62,110,24,8,Muted,false);
            stopBelow=new NumericUpDown{Minimum=35,Maximum=60,Value=50,Location=new Point(680,59),Size=new Size(56,25)};parent.Controls.Add(stopBelow);AddLabel(parent,"°C（35–60）",745,62,160,24,8,Muted,false);
            AddLabel(parent,"非零最低输出",568,90,110,24,8,Muted,false);
            minimumRun=new NumericUpDown{Minimum=10,Maximum=40,Value=20,Location=new Point(680,87),Size=new Size(56,25)};parent.Controls.Add(minimumRun);AddLabel(parent,"%（10–40）",745,90,160,24,8,Muted,false);
            hints.SetToolTip(stopBelow,"低于此温度允许停转；达到时至少按最低运行输出运行。无额外 5°C 回差。边界附近可能反复启停，请合理设置节点。");
            hints.SetToolTip(minimumRun,"请求低于此输出时：允许停转且温度低于上限则执行 0%；否则执行此最低输出。");
            allowStop.CheckedChanged+=delegate{RefreshActions();};stopBelow.ValueChanged+=delegate{RefreshActions();};minimumRun.ValueChanged+=delegate{RefreshActions();};
        }
        void InitializeConfigurationUi(Control parent){
            editCurve=Button(parent,"编辑温度曲线…",568,76,156,31,false);
            saveConfig=Button(parent,"保存配置",738,76,182,31,false);
            curveSummary=AddLabel(parent,"",20,113,900,26,9,Ink,false);
            editCurve.Click+=delegate{
                CurvePoint[] points;try{points=ParseCurve(curveText.Text);ControlPolicy.ValidateCurve(points);}catch{points=new UserConfiguration().Curve;}
                using(var editor=new CurveEditor(points)){if(editor.ShowDialog(this)==DialogResult.OK)curveText.Text=FormatCurve(editor.Result);}
            };
            saveConfig.Click+=delegate{SaveConfigurationUi();};
            hints.SetToolTip(saveConfig,"记住模式、曲线及停转规则。保存不会改变当前风扇；启动仍需人工点击应用。");
        }
        static string FormatCurve(CurvePoint[] points){var text=new StringBuilder();foreach(var p in points){if(text.Length>0)text.Append("; ");text.Append(p.Temperature).Append(':').Append(p.Percent);}return text.ToString();}
        UserConfiguration ReadConfigurationUi(){
            return new UserConfiguration{Mode=controlMode.SelectedIndex,Percent=(int)number.Value,Rpm=(int)rpmTarget.Value,Curve=ParseCurve(curveText.Text),AllowFanStop=allowStop.Checked,StopBelowC=(int)stopBelow.Value,MinimumRunningPercent=(int)minimumRun.Value};
        }
        string ConfigurationSignature(){return controlMode.SelectedIndex+"|"+number.Value+"|"+rpmTarget.Value+"|"+allowStop.Checked+"|"+stopBelow.Value+"|"+minimumRun.Value+"|"+curveText.Text;}
        void LoadConfigurationUi(){
            string message="预览默认设置，未读写配置";
            var value=preview?new UserConfiguration():ConfigurationStore.Load(ConfigurationStore.DefaultPath,out message);
            controlMode.SelectedIndex=value.Mode;number.Value=value.Percent;rpmTarget.Value=value.Rpm;
            allowStop.Checked=value.AllowFanStop;stopBelow.Value=value.StopBelowC;minimumRun.Value=value.MinimumRunningPercent;curveText.Text=FormatCurve(value.Curve);
            savedConfiguration=!preview && message.StartsWith("已加载配置")?ConfigurationSignature():null;
            configStatus.Text=message;hints.SetToolTip(configStatus,message+"\n"+ConfigurationStore.DefaultPath);
        }
        void RefreshConfigurationUi(){
            if(curveSummary==null)return;
            try{
                var points=ParseCurve(curveText.Text);ControlPolicy.ValidateCurve(points);
                var summary=new StringBuilder();foreach(var p in points){if(summary.Length>0)summary.Append("   ·   ");summary.Append(p.Temperature).Append("°C → ").Append(p.Percent).Append('%');}
                curveSummary.Text=summary.ToString();hints.SetToolTip(curveSummary,curveSummary.Text);
            }catch{curveSummary.Text="曲线无效，请点击“编辑温度曲线”修正节点。";}
            if(savedConfiguration!=null)configStatus.Text=ConfigurationSignature()==savedConfiguration?"✓ 配置已保存 · 启动后仍需应用":"配置有修改 · 点击保存或应用";
            stopBelow.Enabled=allowStop.Checked && !closing;
        }
        bool SaveConfigurationUi(){
            if(preview)return true;
            try{var value=ReadConfigurationUi();ConfigurationStore.Save(ConfigurationStore.DefaultPath,value);savedConfiguration=ConfigurationSignature();configStatus.Text="✓ 配置已保存 · 保存不等于应用";return true;}
            catch(Exception ex){configStatus.Text="配置保存失败";hints.SetToolTip(configStatus,ex.Message);MessageBox.Show(this,"配置未保存："+ex.Message,"保存配置",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
        }
    }
}
