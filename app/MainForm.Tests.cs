using System;
using System.Drawing;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        internal void VerifyShutdownContract(string directory){
            if(!preview)throw new InvalidOperationException("Shutdown test must not access hardware.");
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();
            run=System.IO.Path.Combine(directory,"ui-shutdown");System.IO.Directory.CreateDirectory(run);session=Program.Token();
            worker=System.Diagnostics.Process.GetCurrentProcess();mayOwnHardware=true;
            var watch=System.Diagnostics.Stopwatch.StartNew();BeginShutdown();
            if(watch.ElapsedMilliseconds>1000 || finishClosing || !closing || apply.Enabled)throw new Exception("Shutdown must wait asynchronously");
            var command=Files.Read<Message>(System.IO.Path.Combine(run,"command.json"));
            if(command==null || command.Command!="QUIT" || command.Session!=session)throw new Exception("Shutdown must request graceful QUIT");
            var frame=new Frame{Session="foreign",Utc=DateTime.UtcNow.ToString("o"),Ended=true,AutoConfirmed=true};
            Files.Write(System.IO.Path.Combine(run,"state.json"),frame);CheckShutdown();
            if(finishClosing)throw new Exception("Foreign recovery closed an owned session");
            frame.Session=session;frame.Manual=true;Files.Write(System.IO.Path.Combine(run,"state.json"),frame);CheckShutdown();
            if(finishClosing)throw new Exception("Manual state counted as restored");
            frame.Manual=false;Files.Write(System.IO.Path.Combine(run,"state.json"),frame);CheckShutdown();
            if(!finishClosing || !recoveryConfirmed)throw new Exception("Confirmed automatic recovery did not complete close");
            Application.DoEvents();
        }
        internal void VerifyUiContract(){
            if(!preview)throw new InvalidOperationException("UI contract test must not use real hardware.");
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();
            using(var graphics=CreateGraphics())if(Math.Abs(canvas.Width-920*graphics.DpiX/96f)>2)throw new Exception("DPI layout must scale all child controls with the window");
            if(stateHeading.Height<TextRenderer.MeasureText("运行状态",stateHeading.Font).Height)throw new Exception("DPI status text clipped");
            last=new Frame{Utc=DateTime.UtcNow.ToString("o"),Connected=true,CanControl=true,CpuC=42,Manual=false};approved=true;ShowFrame(last);
            controlMode.SelectedIndex=0;
            slider.Value=75;
            if(number.Value!=75 || presets.SelectedIndex!=-1 || slider.Minimum!=0 || slider.Maximum!=100)throw new Exception("UI percentage binding");
            apply.PerformClick();
            if(testCommand==null || testCommand.Command!="SET" || testCommand.Percent!=75)throw new Exception("Apply button command");
            last.Manual=true;last.Owned=true;last.SetPercent=75;ShowFrame(last);automatic.PerformClick();
            if(testCommand.Command!="AUTO")throw new Exception("Automatic button command");
            for(int i=0;i<PresetValues.Length;i++){
                presets.SelectedIndex=i;apply.PerformClick();
                if(number.Value!=PresetValues[i] || slider.Value!=PresetValues[i] || testCommand.Command!="SET" || testCommand.Percent!=PresetValues[i])throw new Exception("Preset binding / apply");
            }
            var recovered=new Frame{Session="test",Utc=DateTime.UtcNow.ToString("o"),Ended=true,AutoConfirmed=true};
            if(!ExitRecoveryConfirmed(recovered,"test") || ExitRecoveryConfirmed(null,"test") || ExitRecoveryConfirmed(recovered,"other"))throw new Exception("Exit recovery identity");
            recovered.Manual=true;if(ExitRecoveryConfirmed(recovered,"test"))throw new Exception("Manual is not recovered");
            recovered.Manual=false;recovered.Utc=DateTime.UtcNow.AddMinutes(-1).ToString("o");if(ExitRecoveryConfirmed(recovered,"test"))throw new Exception("Stale recovery");
            controlMode.SelectedIndex=1;rpmTarget.Value=3000;apply.PerformClick();if(testCommand.Command!="RPM" || testCommand.Rpm!=3000)throw new Exception("RPM command");
            controlMode.SelectedIndex=2;apply.PerformClick();if(testCommand.Command!="CURVE" || testCommand.Curve.Length!=6)throw new Exception("Curve command");
            controlMode.SelectedIndex=0;slider.Value=0;apply.PerformClick();if(testCommand.Command!="SET" || testCommand.Percent!=0)throw new Exception("Zero command");Hide();
        }
        internal void VerifyUiStates(){
            if(!preview)throw new InvalidOperationException("UI state test must not use hardware.");
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();approved=true;
            var f=new Frame{Utc=DateTime.UtcNow.ToString("o"),Connected=true,CanControl=true,CpuC=45};ShowFrame(f);controlMode.SelectedIndex=2;
            if(apply.Text!="保存并应用曲线" || !apply.Enabled || !stateHeading.Text.Contains("尚未应用") || percentPanel.Visible || rpmPanel.Visible || !curvePanel.Visible)throw new Exception("Draft state / only active editor visible");
            curveText.Text="35:0; 85:50; 95:100";
            if(apply.Enabled || !validation.Text.Contains("20–84"))throw new Exception("Inline curve error");
            curveText.Text="35:0; 45:30; 55:50; 65:70; 75:100";
            f.Manual=f.Owned=true;f.ControlMode="CURVE";f.SetPercent=30;f.ActiveSettingsKey=ControlPolicy.SettingsKey(Draft());ShowFrame(f);
            if(!stateHeading.Text.StartsWith("已生效：温度曲线") || stateHeading.Text.Contains("未应用"))throw new Exception("Confirmed applied state");
            f.SafetyStatus="静音补风：已临时提高至至少 30%；降温后回到原设置。";ShowFrame(f);
            if(!apply.Enabled || !note.Text.Contains("无需重连"))throw new Exception("Supplement must not disable curve controls");
            f.SafetyStatus=null;
            f.RequestedOutputPercent=13;f.SetPercent=0;f.RuleCode="STOP";f.DecisionReason="请求低于最低运行输出 20%，且温度低于停转上限 50°C，执行停转。";ShowFrame(f);
            if(!stateHeading.Text.Contains("请求 13% → 目标 0%") || !stateHeading.Text.Contains("允许停转") || note.Text.Contains("补风"))throw new Exception("Transparent stop decision display");
            curveText.Text="35:0; 45:35; 55:50; 65:70; 75:100";
            if(!stateHeading.Text.Contains("编辑未应用"))throw new Exception("Dirty state");
            f.Manual=f.Owned=f.CanControl=false;f.AutoConfirmed=true;f.Error="控制后台失联，已执行独立恢复。";ShowFrame(f);
            if(apply.Enabled || !reconnect.Enabled || !stateHeading.Text.Contains("保护已停止") || !curveText.Enabled)throw new Exception("Protected state / reconnect / editable draft");
            f.Utc=DateTime.UtcNow.AddSeconds(-10).ToString("o");RefreshActions();
            if(apply.Enabled || !reconnect.Enabled || !stateHeading.Text.Contains("未连接"))throw new Exception("Disconnected state");
            ShowFrame(f);if(Convert.ToString(fans.Rows[0].Cells[1].Value)!="--")throw new Exception("Stale telemetry must never display zero RPM");
            f.Utc=DateTime.UtcNow.ToString("o");f.Connected=true;f.CpuC=null;ShowFrame(f);if(cpuBadge.Text!="-- °C")throw new Exception("Missing temperature retained previous reading");
            Hide();
        }
    }
}
