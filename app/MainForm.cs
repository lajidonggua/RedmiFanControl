using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm : Form {
        readonly bool preview;string run,session;int sequence,pending;Process worker;
        readonly Timer timer=new Timer();readonly Stopwatch heartbeat=Stopwatch.StartNew();
        DataGridView fans,sensors;Label status,mode,note,updated,cpuBadge;TrackBar slider;NumericUpDown number;
        Button apply,automatic,reconnect,exit;ComboBox presets;NotifyIcon tray;
        ComboBox controlMode;NumericUpDown rpmTarget;TextBox curveText;
        Frame last;double low=double.MaxValue,high=double.MinValue;int peak1,peak2;
        bool closing,approved,captured; int tick;
        Message testCommand;
        static readonly int[] PresetValues={0,30,40,50,60,80,100};
        internal bool ApplyDefaultCurveOnConnect;
        Label stateHeading,validation;Panel statePanel;
        readonly ToolTip hints=new ToolTip();readonly Stopwatch requestAge=new Stopwatch(),restartAge=new Stopwatch();
        bool restarting;
        internal System.Threading.EventWaitHandle WakeSignal;
        string reconnectProblem;
        static Color Blue=Color.FromArgb(33,103,200),Ink=Color.FromArgb(35,45,57),Muted=Color.FromArgb(100,111,125);
        public MainForm(bool isPreview){
            preview=isPreview;InitializeLayout();
            slider.ValueChanged+=delegate{number.Value=slider.Value;presets.SelectedIndex=Array.IndexOf(PresetValues,slider.Value);RefreshActions();};
            number.ValueChanged+=delegate{slider.Value=(int)number.Value;};
            presets.SelectedIndexChanged+=delegate{if(presets.SelectedIndex>=0)slider.Value=PresetValues[presets.SelectedIndex];};
            controlMode.SelectedIndexChanged+=delegate{RefreshActions();};curveText.TextChanged+=delegate{RefreshActions();};rpmTarget.ValueChanged+=delegate{RefreshActions();};
            apply.Click+=delegate{ApplyManual();};automatic.Click+=delegate{Send("AUTO",0);};reconnect.Click+=delegate{Reconnect();};exit.Click+=delegate{Close();};
            var menu=new ContextMenuStrip();menu.Items.Add("显示主窗口",null,delegate{ShowMain();});
            menu.Items.Add("恢复自动模式",null,delegate{Send("AUTO",0);});menu.Items.Add("退出并恢复自动",null,delegate{Close();});
            tray=new NotifyIcon{Icon=Icon,Text="Redmi Fan Control",ContextMenuStrip=menu,Visible=!preview};
            tray.DoubleClick+=delegate{ShowMain();};
            Resize+=delegate{if(WindowState==FormWindowState.Minimized)Hide();};
            timer.Interval=500;timer.Tick+=delegate{Poll();};
            Shown+=delegate{if(!preview){timer.Start();if(StartInTray){ShowInTaskbar=false;BeginInvoke(new Action(Hide));stateHeading.Text="登录自启 · 尚未连接硬件";note.Text="需要监测或调速时点击“重新连接后台”，正常确认 UAC；没有自动应用手动设置。";status.Text="托盘待命 · 系统自动温控";}else BeginInvoke(new Action(Connect));}};
            FormClosing+=ClosingSafe;
            LoadConfigurationUi();
            RefreshActions();
        }
        Panel Section(string title,int x,int y,int width,int height){
            var p=new Panel{Location=new Point(x,y),Size=new Size(width,height),BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle};canvas.Controls.Add(p);
            AddLabel(p,title,18,10,width-35,25,11,Ink,true);return p;
        }
        static Label AddLabel(Control parent,string text,int x,int y,int w,int h,float size,Color color,bool bold){
            var label=new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=color,AutoEllipsis=true};parent.Controls.Add(label);return label;
        }
        static Button Button(Control parent,string text,int x,int y,int w,int h,bool primary){
            var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=primary?Blue:Color.White,ForeColor=primary?Color.White:Ink,Cursor=Cursors.Hand};
            b.FlatAppearance.BorderColor=primary?Blue:Color.FromArgb(208,215,224);parent.Controls.Add(b);return b;
        }
        static DataGridView Grid(Control parent,int x,int y,int w,int h,string[] names,float[] weights){
            var grid=new DataGridView{Location=new Point(x,y),Size=new Size(w,h),BackgroundColor=Color.White,BorderStyle=BorderStyle.None,ReadOnly=true,
                AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AllowUserToResizeColumns=false,RowHeadersVisible=false,
                EnableHeadersVisualStyles=false,ScrollBars=ScrollBars.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,ColumnHeadersHeight=26,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,TabStop=false};
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(244,246,249);grid.ColumnHeadersDefaultCellStyle.ForeColor=Muted;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
            grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(240,245,252);grid.DefaultCellStyle.SelectionForeColor=Ink;grid.DefaultCellStyle.ForeColor=Ink;
            grid.DefaultCellStyle.Font=new Font("Microsoft YaHei UI",9);grid.GridColor=Color.FromArgb(234,238,243);grid.RowTemplate.Height=28;
            for(int i=0;i<names.Length;i++){int index=grid.Columns.Add("c"+i,names[i]);grid.Columns[index].FillWeight=weights[i];grid.Columns[index].SortMode=DataGridViewColumnSortMode.NotSortable;}
            parent.Controls.Add(grid);return grid;
        }
        void Connect(){
            if(preview || closing)return;
            if(worker!=null){
                if(!worker.HasExited)return;
                ReadRecoveryConfirmation();
                if(mayOwnHardware && !recoveryConfirmed){reconnectProblem="上一次调速的自动恢复尚未确认，暂不启动新后台。请稍后重试或检查日志。";RefreshActions();return;}
                worker.Dispose();worker=null;
            }
            restarting=false;reconnectProblem=null;captured=false;approved=false;mayOwnHardware=false;recoveryConfirmed=false;
            run=null;last=null;ClearValues();
            if(!File.Exists(AppPaths.DriverLibrary)){reconnectProblem="尚未安装 PawnIO 驱动。请打开“设置 / 关于”中的官网，安装后点击连接硬件。";RefreshActions();return;}
            try{RuntimeModules.Prepare(AppPaths.Modules);}catch(Exception ex){reconnectProblem="准备运行模块失败："+ex.Message;RefreshActions();return;}
            run=Path.Combine(AppPaths.Runs,"app-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            Directory.CreateDirectory(run);session=Program.Token();sequence=0;pending=0;last=null;
            Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=++sequence});
            status.Text="请在 Windows UAC 中授权硬件读取与风扇控制…";reconnect.Enabled=false;
            try{using(var process=Process.GetCurrentProcess()){
                worker=Program.Spawn("--worker",run,session,process.Id,process.StartTime.ToUniversalTime().Ticks,true);
                Files.Write(Path.Combine(run,"launch.json"),new {ui_pid=process.Id,worker_pid=worker.Id,session=session,utc=DateTime.UtcNow.ToString("o"),version=Application.ProductVersion});
            }}
            catch(Exception ex){reconnectProblem="未连接（可重新授权）："+ex.Message;status.Text=reconnectProblem;reconnect.Enabled=true;}
        }
        void Reconnect(){
            if(preview || closing || restarting)return;
            ApplyDefaultCurveOnConnect=false; // Reconnection NEVER silently reapplies a stopped policy.
            if(worker==null || worker.HasExited){Connect();return;}
            try{
                reconnectProblem=null;Send("QUIT",0);restarting=true;restartAge.Restart();RefreshActions();
            }catch(Exception ex){status.Text="重连请求失败："+ex.Message;}
        }
        Message Draft(){
            var message=ReadConfigurationUi().Request();new ControlPolicy(message,0);return message;
        }
        void RefreshActions(){
            if(controlMode==null || apply==null || stateHeading==null || reconnect==null)return;
            RefreshConfigurationUi();
            Message draft=null;string invalid=null;try{draft=Draft();}catch(Exception ex){invalid=ex.Message;}
            validation.Text=invalid??"修改后点击“保存并应用”；仅保存不会改变当前风扇。";
            validation.ForeColor=invalid==null?Color.FromArgb(36,129,83):Color.Firebrick;hints.SetToolTip(validation,validation.Text);
            bool live=Presentation.IsLive(last);
            bool waiting=live && pending>0 && last.Ack<pending;
            bool ready=live && last.CanControl && !waiting && !closing && !restarting;
            // Editing remains available during protection/disconnection. Only execution is gated.
            slider.Enabled=number.Enabled=presets.Enabled=controlMode.SelectedIndex==0 && !closing;
            rpmTarget.Enabled=controlMode.SelectedIndex==1 && !closing;curveText.Enabled=controlMode.SelectedIndex==2 && !closing;
            editCurve.Enabled=controlMode.SelectedIndex==2 && !closing;
            percentPanel.Visible=controlMode.SelectedIndex==0;rpmPanel.Visible=controlMode.SelectedIndex==1;curvePanel.Visible=controlMode.SelectedIndex==2;
            saveConfig.Enabled=!closing && invalid==null;allowStop.Enabled=minimumRun.Enabled=controlMode.Enabled=!closing;
            apply.Text=waiting?"等待硬件确认…":controlMode.SelectedIndex==2?"保存并应用曲线":controlMode.SelectedIndex==1?"保存并应用转速":"保存并应用 "+number.Value+"%";
            apply.Enabled=ready && invalid==null;
            apply.BackColor=apply.Enabled?Blue:Color.FromArgb(218,224,232);apply.ForeColor=apply.Enabled?Color.White:Muted;
            automatic.Enabled=live && last.Owned && !closing && !restarting;
            reconnect.Enabled=!closing && !restarting && (!live || !last.CanControl || (waiting && requestAge.Elapsed.TotalSeconds>5));
            reconnect.Text=restarting?"正在重连…":live?"重新连接":"连接硬件";
            var notice=Presentation.Describe(last,draft,waiting,requestAge.Elapsed.TotalSeconds);
            string title=notice.Title,detail=notice.Detail;
            Color tint=notice.Tone==NoticeTone.Success?Color.FromArgb(227,245,233):notice.Tone==NoticeTone.Warning?Color.FromArgb(255,241,218):notice.Tone==NoticeTone.Error?Color.FromArgb(255,233,224):Color.FromArgb(231,240,253);
            if(restarting){title="正在恢复自动并重新连接";detail="恢复确认后连接，完成后请再次应用设置。";}
            if(closing){title="正在退出 · 恢复系统自动";detail="等待硬件确认，完成后窗口会自动关闭。";}
            if(reconnectProblem!=null){title="重新连接尚未完成";detail=reconnectProblem;}
            stateHeading.Text=title;note.Text=detail;statePanel.BackColor=tint;hints.SetToolTip(note,detail);hints.SetToolTip(stateHeading,title);
            hints.SetToolTip(apply,apply.Enabled?"将当前选择的模式应用到两路风扇。":invalid??detail);
        }
        void ApplyManual(){
            if(last==null || !last.CanControl || !Files.Fresh(last.Utc,3))return;
            Message draft;try{draft=Draft();}catch{RefreshActions();return;}
            if(!approved){
                if(MessageBox.Show(this,"即将由本程序接管两路风扇。\n\n实际输出受静音与温度规则约束；启动时可能短暂全速。异常或达到 95°C 会恢复系统自动。\n\n这是特定机型的实验调速功能，请留意温度。随时可恢复系统自动；退出也会恢复。继续应用？","首次启用自定义调速",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                approved=true;
            }
            if(!preview && !SaveConfigurationUi())return;SendMessage(draft);
        }
        internal static CurvePoint[] ParseCurve(string text){
            return CurveFormat.Parse(text);
        }
        void Send(string command,int percent){
            SendMessage(new Message{Command=command,Percent=percent});
        }
        void SendMessage(Message message){
            if(preview){testCommand=message;return;}
            if(run==null || closing)return;
            try{pending=++sequence;message.Session=session;message.Sequence=pending;if(message.Command=="SET" || message.Command=="RPM" || message.Command=="CURVE")mayOwnHardware=true;Files.Write(Path.Combine(run,"command.json"),message);requestAge.Restart();status.Text=message.Command=="AUTO"?"正在恢复系统自动温控…":"正在应用设置，等待后台确认…";RefreshActions();}
            catch(Exception ex){status.Text=ex.Message;}
        }
        void Poll(){
            if(WakeSignal!=null && WakeSignal.WaitOne(0) && !closing)ShowMain();
            if(preview || run==null)return;
            try{
                if(heartbeat.ElapsedMilliseconds>=750){Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=++sequence});heartbeat.Restart();}
                var frame=Files.Read<Frame>(Path.Combine(run,"state.json"));
                if(frame!=null && frame.Session==session){
                    last=frame;ShowFrame(frame);
                    if(ApplyDefaultCurveOnConnect && frame.CanControl && Files.Fresh(frame.Utc,3)){
                        ApplyDefaultCurveOnConnect=false;
                        controlMode.SelectedIndex=2;
                        curveText.Text="35:0; 45:30; 55:50; 65:70; 75:100";
                        // The explicit launch option represents this user's approval of this exact curve.
                        approved=true;ApplyManual();
                    }
                }
                bool workerDead=worker!=null && worker.HasExited;
                if(ExitRecoveryConfirmed(last,session))recoveryConfirmed=true;
                if(workerDead){var recovery=Files.Read<Frame>(Path.Combine(run,"guardian.json"));if(ExitRecoveryConfirmed(recovery,session))recoveryConfirmed=true;}
                if(closing){RefreshActions();CheckShutdown();return;}
                if(restarting){
                    if(workerDead && (recoveryConfirmed || !mayOwnHardware)){Connect();return;}
                    if(restartAge.Elapsed.TotalSeconds>12){restarting=false;reconnectProblem="旧后台退出或自动恢复尚未确认，未启动第二个控制器；请检查日志。";status.Text=reconnectProblem;}
                }
                if(!Presentation.IsLive(last) || (workerDead && !last.Ended)){
                    if(workerDead && last!=null && !last.Ended){last.Connected=false;last.CanControl=false;}
                    ClearValues();apply.Enabled=automatic.Enabled=slider.Enabled=number.Enabled=false;mode.Text="连接中断";status.Text="监测数据暂不可用，不显示旧值或零值。";
                    var recovery=Files.Read<Frame>(Path.Combine(run,"guardian.json"));
                    if(recovery!=null && recovery.Session==session && recovery.AutoConfirmed && recovery.Ended){status.Text="独立看护已确认恢复自动；请重新连接。";}
                }
                ++tick;RefreshActions();
                if(!captured && last!=null && last.CanControl && Visible && Files.Fresh(last.Utc,3)){
                    using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(run,"window.png"),System.Drawing.Imaging.ImageFormat.Png);}captured=true;
                }
            }catch(Exception ex){ClearValues();status.Text="监测异常："+ex.Message;if(closing)CheckShutdown();}
        }
        void ClearValues(){
            foreach(DataGridViewRow row in fans.Rows)for(int i=1;i<row.Cells.Count;i++)row.Cells[i].Value="--";
            sensors.Rows[0].Cells[1].Value="--";cpuBadge.Text="-- °C";fanHint.Text="没有有效实时数据；-- 不表示风扇停转。";
        }
        void ShowFrame(Frame f){
            last=f;
            if(!Presentation.IsLive(f)){
                ClearValues();apply.Enabled=automatic.Enabled=slider.Enabled=number.Enabled=false;
                mode.Text=f.AutoConfirmed?"已恢复自动":"未连接";status.Text=f.Error??f.Status??"正在连接…";return;
            }
            peak1=Math.Max(peak1,f.Rpm1);peak2=Math.Max(peak2,f.Rpm2);
            string text=f.Manual?(f.Owned?(f.ControlMode=="RPM"?"目标 "+f.RequestedRpm+" RPM":f.ControlMode=="CURVE"?"温度曲线 "+f.SetPercent+"%":"手动 "+f.SetPercent+"% "):"外部控制"):"系统自动";
            fans.Rows[0].SetValues("风扇 1",f.Rpm1+" RPM",f.Output1.ToString("0.#")+"%",Presentation.FanState(f.Rpm1,f.Output1,f.Manual));
            fans.Rows[1].SetValues("风扇 2",f.Rpm2+" RPM",f.Output2.ToString("0.#")+"%",Presentation.FanState(f.Rpm2,f.Output2,f.Manual));
            fanHint.Text="本次最高 "+peak1+" / "+peak2+" RPM · 0 = 实测停转；-- = 暂无有效读数";
            if(f.CpuC.HasValue){double t=f.CpuC.Value;low=Math.Min(low,t);high=Math.Max(high,t);cpuBadge.Text=t.ToString("0.0")+" °C";
                sensors.Rows[0].SetValues("Tctl/Tdie",low.ToString("0.0")+" / "+high.ToString("0.0")+" °C");}
            else{cpuBadge.Text="-- °C";sensors.Rows[0].Cells[1].Value="--";}
            mode.Text=text;mode.ForeColor=f.Manual?Blue:Color.FromArgb(36,129,83);
            status.Text=f.Error??f.Status??"已连接";DateTime received;updated.Text=DateTime.TryParse(f.Utc,out received)?"更新 "+received.ToLocalTime().ToString("HH:mm:ss"):"";
            if(!preview)tray.Text=("CPU "+(f.CpuC.HasValue?f.CpuC.Value.ToString("0.0"):"--")+"°C | "+f.Rpm1+" / "+f.Rpm2+" RPM");
            RefreshActions();
        }
        public void SavePreview(string path,string scenario="--preview"){
            if(scenario=="--preview-maximized")Opacity=0;
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();
            if(scenario=="--preview-maximized"){WindowState=FormWindowState.Maximized;Application.DoEvents();}
            Text="Redmi Fan Control · 界面预览（示例数据）";
            var sample=new Frame{Utc=DateTime.UtcNow.ToString("o"),Connected=true,CanControl=true,Manual=false,Rpm1=2652,Rpm2=2671,Output1=30.5,Output2=34.4,CpuC=42.5,Status="界面预览 · 示例数据，未连接硬件"};
            if(scenario=="--preview-stopped"){sample.Rpm1=sample.Rpm2=0;sample.Output1=sample.Output2=0;sample.CpuC=38;}
            if(scenario=="--preview-percent"){controlMode.SelectedIndex=0;number.Value=30;}
            if(scenario=="--preview-offline"){sample.Connected=false;sample.CanControl=false;}
            ShowFrame(sample);RefreshActions();
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Hide();
        }
    }
}
