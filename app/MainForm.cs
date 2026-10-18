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
        static readonly int[] PresetValues={30,40,50,60,80,100};
        internal bool ApplyDefaultCurveOnConnect;
        Label stateHeading,validation;Panel statePanel;
        readonly ToolTip hints=new ToolTip();readonly Stopwatch requestAge=new Stopwatch(),restartAge=new Stopwatch();
        bool restarting;
        string reconnectProblem;
        static Color Blue=Color.FromArgb(33,103,200),Ink=Color.FromArgb(35,45,57),Muted=Color.FromArgb(100,111,125);
        public MainForm(bool isPreview){
            preview=isPreview;Text="Redmi Fan Control · 0.8.1";ClientSize=new Size(980,770);MinimumSize=Size;MaximumSize=Size;
            StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
            Font=new Font("Microsoft YaHei UI",9);BackColor=Color.FromArgb(246,248,251);AutoScaleMode=AutoScaleMode.Dpi;
            Icon=AppBrand.WindowIcon();
            var header=new Panel{Location=new Point(0,0),Size=new Size(980,88),BackColor=Color.White};Controls.Add(header);
            var logo=new PictureBox{Image=AppBrand.HeaderImage(),Location=new Point(22,19),Size=new Size(42,42),SizeMode=PictureBoxSizeMode.Zoom};header.Controls.Add(logo);
            Disposed+=delegate{logo.Image.Dispose();Icon.Dispose();hints.Dispose();};
            AddLabel(header,"Redmi Fan Control",73,14,390,30,18,Ink,true);
            AddLabel(header,"Redmi Book Pro 15 2022  ·  Ryzen 7 6800H  ·  双风扇联动",76,49,660,22,9,Muted,false);
            mode=AddLabel(header,"等待连接",753,24,200,25,10,Muted,true);mode.TextAlign=ContentAlignment.MiddleRight;
            InitializeAutostart(header);
            var left=Section("风扇",20,106,568,192);
            AddLabel(left,"实时转速与实际输出",20,34,500,22,9,Muted,false);
            fans=Grid(left,14,67,540,109,new string[]{"风扇","当前转速","本次最高","输出","模式"},new float[]{17,26,24,17,24});
            fans.Rows.Add("风扇 1","--","--","--","--");fans.Rows.Add("风扇 2","--","--","--","--");
            var right=Section("温度传感器",608,106,352,192);
            cpuBadge=AddLabel(right,"-- °C",22,36,300,37,22,Blue,true);
            sensors=Grid(right,14,86,324,76,new string[]{"传感器","当前","最低 / 最高"},new float[]{41,23,41});
            sensors.Rows.Add("CPU (Tctl/Tdie)","--","--");
            AddLabel(right,"只显示已核验的传感器，不填充未知温度",18,165,320,20,8,Muted,false);
            var controls=Section("1. 手动输出与静音规则",20,316,940,124);
            AddLabel(controls,"手动输出",20,35,90,24,10,Ink,true);
            slider=new TrackBar{Minimum=0,Maximum=100,Value=60,TickFrequency=10,SmallChange=1,LargeChange=5,Location=new Point(111,30),Size=new Size(346,48),Enabled=false};controls.Controls.Add(slider);
            number=new NumericUpDown{Minimum=0,Maximum=100,Value=60,Location=new Point(466,35),Size=new Size(65,29),Enabled=false};controls.Controls.Add(number);
            AddLabel(controls,"%",537,38,24,24,10,Muted,false);
            InitializeStopRules(controls);
            AddLabel(controls,"预设",24,87,45,23,9,Muted,false);
            presets=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(74,82),Size=new Size(166,27)};
            foreach(int value in PresetValues)presets.Items.Add((value==100?"全速":"手动输出")+" · "+value+"%");presets.SelectedIndex=3;controls.Controls.Add(presets);
            configStatus=AddLabel(controls,"尚未保存配置",257,84,300,27,8,Muted,false);
            var advanced=Section("2. 选择模式并编辑 → 3. 点击对应的应用按钮",20,456,940,206);
            controlMode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(20,43),Size=new Size(235,27)};
            controlMode.Items.AddRange(new object[]{"百分比 · 使用上方 0–100%","目标 RPM · 以较慢一路为准","CPU 温度曲线 · 输出百分比"});controlMode.SelectedIndex=0;advanced.Controls.Add(controlMode);
            AddLabel(advanced,"目标 RPM",278,46,85,24,9,Muted,false);
            rpmTarget=new NumericUpDown{Minimum=0,Maximum=6000,Increment=100,Value=3000,Location=new Point(365,43),Size=new Size(95,27)};advanced.Controls.Add(rpmTarget);
            AddLabel(advanced,"0 = 停转；非零 1000–6000。两路联动，并非分别恒定转速。",478,44,442,36,8,Muted,false);
            AddLabel(advanced,"无隐藏回差：60°C 最低 30% · 80°C 满速 · 95°C 恢复自动",20,81,530,23,8,Muted,false);
            curveText=new TextBox{Text="35:0; 45:30; 55:50; 65:70; 75:100",Visible=false};advanced.Controls.Add(curveText);
            InitializeConfigurationUi(advanced);
            validation=AddLabel(advanced,"",20,150,530,42,9,Muted,false);
            apply=Button(advanced,"应用百分比",568,153,156,37,true);apply.Enabled=false;
            automatic=Button(advanced,"恢复系统自动",738,153,182,37,false);automatic.Enabled=false;
            var notice=new Panel{Location=new Point(20,676),Size=new Size(940,56),BackColor=Color.FromArgb(231,240,253)};Controls.Add(notice);statePanel=notice;
            stateHeading=AddLabel(notice,"等待连接硬件后台",12,4,915,24,11,Ink,true);
            note=AddLabel(notice,"准备好后才可应用；选择模式或编辑曲线不代表已生效。",12,30,915,23,9,Color.FromArgb(50,85,130),false);
            status=AddLabel(this,"正在准备温度与风扇监测…",22,741,510,25,8,Muted,false);
            updated=AddLabel(this,"",550,741,170,25,8,Muted,false);
            reconnect=Button(this,"重新连接后台",731,737,132,29,false);exit=Button(this,"退出",875,737,85,29,false);
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
            var p=new Panel{Location=new Point(x,y),Size=new Size(width,height),BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle};Controls.Add(p);
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
                ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,ColumnHeadersHeight=28,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,TabStop=false};
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(244,246,249);grid.ColumnHeadersDefaultCellStyle.ForeColor=Muted;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
            grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(240,245,252);grid.DefaultCellStyle.SelectionForeColor=Ink;grid.DefaultCellStyle.ForeColor=Ink;
            grid.DefaultCellStyle.Font=new Font("Microsoft YaHei UI",9);grid.GridColor=Color.FromArgb(234,238,243);grid.RowTemplate.Height=37;
            for(int i=0;i<names.Length;i++){int index=grid.Columns.Add("c"+i,names[i]);grid.Columns[index].FillWeight=weights[i];grid.Columns[index].SortMode=DataGridViewColumnSortMode.NotSortable;}
            parent.Controls.Add(grid);return grid;
        }
        void Connect(){
            if(preview || closing)return;
            if(worker!=null){try{if(!worker.HasExited)return;}catch{}worker.Dispose();worker=null;}
            restarting=false;reconnectProblem=null;captured=false;approved=false;run=Path.Combine(Program.Root,"runs","app-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
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
            validation.Text=invalid??(controlMode.SelectedIndex==2?"✓ 曲线格式有效。点击右侧“应用温度曲线”后才生效。":"✓ 设置有效。点击右侧应用按钮后才生效。");
            validation.ForeColor=invalid==null?Color.FromArgb(36,129,83):Color.Firebrick;hints.SetToolTip(validation,validation.Text);
            bool live=last!=null && last.Connected && !last.Ended && Files.Fresh(last.Utc,3);
            bool waiting=live && pending>0 && last.Ack<pending;
            bool ready=live && last.CanControl && !waiting && !closing && !restarting;
            // Editing remains available during protection/disconnection. Only execution is gated.
            slider.Enabled=number.Enabled=presets.Enabled=controlMode.SelectedIndex==0 && !closing;
            rpmTarget.Enabled=controlMode.SelectedIndex==1 && !closing;curveText.Enabled=controlMode.SelectedIndex==2 && !closing;
            editCurve.Enabled=controlMode.SelectedIndex==2 && !closing;
            apply.Text=waiting?"正在等待确认…":controlMode.SelectedIndex==2?"应用温度曲线":controlMode.SelectedIndex==1?"应用目标转速":"应用 "+number.Value+"% 输出";
            apply.Enabled=ready && invalid==null;
            apply.BackColor=apply.Enabled?Blue:Color.FromArgb(218,224,232);apply.ForeColor=apply.Enabled?Color.White:Muted;
            automatic.Enabled=live && last.Owned && !closing && !restarting;
            reconnect.Enabled=!closing && !restarting && (!live || !last.CanControl || (waiting && requestAge.Elapsed.TotalSeconds>5));
            reconnect.Text=restarting?"正在安全重连…":"重新连接后台";
            string title,detail;Color tint;
            if(restarting){title="正在恢复自动并重新连接";detail="正在等待旧后台正常退出，不会强制结束进程；重新连接后请再次点击应用。";tint=Color.FromArgb(231,240,253);}
            else if(!live){title=last!=null && last.AutoConfirmed?"未连接 · 已确认恢复系统自动":"未连接 · 设置尚未应用";detail=(last!=null?last.Error:null)??"请点击“重新连接后台”并确认 UAC。没有实时数据时不会显示旧转速。";tint=Color.FromArgb(255,241,218);}
            else if(!last.CanControl){
                if(last.Error!=null){title=last.AutoConfirmed?"保护已停止调速 · 已恢复系统自动":"保护已停止调速 · 请检查恢复状态";detail=last.Error+" 点击“重新连接后台”恢复连接后，再手动应用。";}
                else if(last.Manual && !last.Owned){title="其他程序正在控制 · 本程序仅监测";detail="请先让其他风扇控制程序恢复自动并退出，避免同时接管。";}
                else if(last.CpuC>=ThermalLimits.EmergencyC){title="温度过高 · 保持系统自动";detail="CPU 达到 95°C，当前不能应用自定义调速。";}
                else{title="暂不能应用 · 等待安全看护就绪";detail="硬件与独立看护正在连接，请稍候；若一直未就绪，可点击重新连接。";}
                tint=Color.FromArgb(255,233,224);
            }
            else if(waiting){title=requestAge.Elapsed.TotalSeconds>5?"后台尚未确认 · 可重新连接":"正在应用 · 等待后台确认";detail="只有收到后台成功确认才会显示“已生效”；编辑设置不等于应用。";tint=Color.FromArgb(231,240,253);}
            else if(last.Manual && last.Owned){
                bool same=draft!=null && last.ActiveSettingsKey==ControlPolicy.SettingsKey(draft);
                title="已生效："+(last.ControlMode=="CURVE"?"温度曲线":last.ControlMode=="RPM"?"目标转速":"百分比")+" · 请求 "+last.RequestedOutputPercent+"% → 目标 "+last.SetPercent+"%";
                if(!same)title+=" · 编辑未应用";
                detail=same?"曲线/设置正在运行。保护优先；可随时点击“恢复系统自动”。":"当前仍运行上次成功应用的设置；点击应用按钮才会更新。";
                if(same && last.SetPercent==0)detail="按设置请求停转中（0%），不计为失速；风扇可能需要短暂滑行才完全停下。";
                tint=same?Color.FromArgb(227,245,233):Color.FromArgb(255,241,218);
                if(last.DecisionReason!=null){title+=" · "+ControlPolicy.RuleName(last.RuleCode);detail=last.DecisionReason+(same?" 无需重连。":" 当前编辑尚未应用。");}
                if(last.SafetyStatus!=null){detail=last.SafetyStatus+(same?" 原设置仍生效，无需重连。":" 当前编辑尚未应用。");tint=Color.FromArgb(255,241,218);}
            }else{title="系统自动 · 当前编辑尚未应用";detail=last.Error??"选择模式、检查设置，再点击对应应用按钮；看到“已生效”才表示后台已接管。";tint=Color.FromArgb(231,240,253);}
            if(invalid!=null){detail="无法应用："+invalid;}
            if(reconnectProblem!=null){title="重新连接尚未完成";detail=reconnectProblem;}
            stateHeading.Text=title;note.Text=detail;statePanel.BackColor=tint;hints.SetToolTip(note,detail);
            hints.SetToolTip(apply,apply.Enabled?"将当前选择的模式应用到两路风扇。":invalid??detail);
        }
        void ApplyManual(){
            if(last==null || !last.CanControl || !Files.Fresh(last.Utc,3))return;
            Message draft;try{draft=Draft();}catch{RefreshActions();return;}
            if(!approved){
                if(MessageBox.Show(this,"执行顺序：当前温度算请求 → 停转/最低运行输出 → 温度下限 → 必要的启动助推。\n\n允许停转且温度低于停转上限时，极低输出按 0% 处理。达到停转上限至少按最低运行输出执行；低于上限立即解除，不再额外等 5°C。60°C 至少 30%、80°C 满速；低于相应门限立即重算，无隐藏补风锁定。\n\n95°C 紧急恢复、真正失速、传感器异常和失联保护保留。启动可能助推 100% / 3 秒，请勿无人值守。应用会保存，重启后仍需人工应用。是否应用？","启用实验调速",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                approved=true;
            }
            if(!preview)SaveConfigurationUi();SendMessage(draft);
        }
        internal static CurvePoint[] ParseCurve(string text){
            string[] parts=text.Split(new char[]{';','；'},StringSplitOptions.RemoveEmptyEntries);var points=new CurvePoint[parts.Length];
            for(int i=0;i<parts.Length;i++){string[] pair=parts[i].Split(':','：');int t,p;if(pair.Length!=2 || !int.TryParse(pair[0].Trim(),out t) || !int.TryParse(pair[1].Trim(),out p))throw new ArgumentException("曲线格式示例：35:0; 45:30; 55:50; 65:70; 75:100");points[i]=new CurvePoint{Temperature=t,Percent=p};}
            return points;
        }
        void Send(string command,int percent){
            SendMessage(new Message{Command=command,Percent=percent});
        }
        void SendMessage(Message message){
            if(preview){testCommand=message;return;}
            if(run==null || closing)return;
            try{pending=++sequence;message.Session=session;message.Sequence=pending;Files.Write(Path.Combine(run,"command.json"),message);requestAge.Restart();status.Text=message.Command=="AUTO"?"正在恢复系统自动温控…":"正在应用设置，等待后台确认…";RefreshActions();}
            catch(Exception ex){status.Text=ex.Message;}
        }
        void Poll(){
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
                if(restarting){
                    if(workerDead && last!=null && last.Ended && (!last.Owned || last.AutoConfirmed)){Connect();return;}
                    if(restartAge.Elapsed.TotalSeconds>12){restarting=false;reconnectProblem="旧后台退出或自动恢复尚未确认，未启动第二个控制器；请检查日志。";status.Text=reconnectProblem;}
                }
                if(last==null || !Files.Fresh(last.Utc,4) || (workerDead && !last.Ended)){
                    ClearValues();apply.Enabled=automatic.Enabled=slider.Enabled=number.Enabled=false;mode.Text="连接中断";status.Text="监测数据暂不可用，不显示旧值或零值。";
                    var recovery=Files.Read<Frame>(Path.Combine(run,"guardian.json"));
                    if(recovery!=null && recovery.Session==session && recovery.AutoConfirmed && recovery.Ended){status.Text="独立看护已确认恢复自动；请重新连接。";}
                }
                ++tick;RefreshActions();
                if(!captured && last!=null && last.CanControl && Visible && Files.Fresh(last.Utc,3)){
                    using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(run,"window.png"),System.Drawing.Imaging.ImageFormat.Png);}captured=true;
                }
            }catch(Exception ex){status.Text="监测异常："+ex.Message;}
        }
        void ClearValues(){
            foreach(DataGridViewRow row in fans.Rows)for(int i=1;i<row.Cells.Count;i++)row.Cells[i].Value="--";
            sensors.Rows[0].Cells[1].Value="--";cpuBadge.Text="-- °C";
        }
        void ShowFrame(Frame f){
            last=f;
            if(!f.Connected || f.Ended || !Files.Fresh(f.Utc,4)){
                ClearValues();apply.Enabled=automatic.Enabled=slider.Enabled=number.Enabled=false;
                mode.Text=f.AutoConfirmed?"已恢复自动":"未连接";status.Text=f.Error??f.Status??"正在连接…";return;
            }
            peak1=Math.Max(peak1,f.Rpm1);peak2=Math.Max(peak2,f.Rpm2);
            string text=f.Manual?(f.Owned?(f.ControlMode=="RPM"?"目标 "+f.RequestedRpm+" RPM":f.ControlMode=="CURVE"?"温度曲线 "+f.SetPercent+"%":"手动 "+f.SetPercent+"% "):"外部控制"):"系统自动";
            fans.Rows[0].SetValues("风扇 1",f.Rpm1+" RPM",peak1+" RPM",f.Output1.ToString("0.#")+"%",text);
            fans.Rows[1].SetValues("风扇 2",f.Rpm2+" RPM",peak2+" RPM",f.Output2.ToString("0.#")+"%",text);
            if(f.CpuC.HasValue){double t=f.CpuC.Value;low=Math.Min(low,t);high=Math.Max(high,t);cpuBadge.Text=t.ToString("0.0")+" °C";
                sensors.Rows[0].SetValues("CPU (Tctl/Tdie)",t.ToString("0.0")+" °C",low.ToString("0.0")+" / "+high.ToString("0.0")+" °C");}
            mode.Text=text;mode.ForeColor=f.Manual?Blue:Color.FromArgb(36,129,83);
            status.Text=f.Error??f.Status??"已连接";updated.Text="更新 "+DateTime.Now.ToString("HH:mm:ss");
            if(!preview)tray.Text=("CPU "+(f.CpuC.HasValue?f.CpuC.Value.ToString("0.0"):"--")+"°C | "+f.Rpm1+" / "+f.Rpm2+" RPM");
            RefreshActions();
        }
        static bool ExitRecoveryConfirmed(Frame frame,string expectedSession){
            return frame!=null && frame.Session==expectedSession && frame.Ended &&
                frame.AutoConfirmed && !frame.Manual && Files.Fresh(frame.Utc,30);
        }
        void ClosingSafe(object sender,FormClosingEventArgs args){
            if(preview)return;
            if(closing){args.Cancel=true;return;}
            closing=true;apply.Enabled=automatic.Enabled=false;
            try{
                if(run!=null){Files.Write(Path.Combine(run,"command.json"),new Message{Session=session,Sequence=++sequence,Command="QUIT"});
                    var wait=Stopwatch.StartNew();
                    while(wait.Elapsed.TotalSeconds<8){
                        var state=Files.Read<Frame>(Path.Combine(run,"state.json"));
                        if(ExitRecoveryConfirmed(state,session))break;
                        if(worker==null || worker.HasExited){var recovery=Files.Read<Frame>(Path.Combine(run,"guardian.json"));if(ExitRecoveryConfirmed(recovery,session))break;}
                        Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=++sequence});Application.DoEvents();System.Threading.Thread.Sleep(100);
                    }
                    var result=Files.Read<Frame>(Path.Combine(run,"state.json"));
                    if(!ExitRecoveryConfirmed(result,session)){
                        var recovery=Files.Read<Frame>(Path.Combine(run,"guardian.json"));
                        if(!ExitRecoveryConfirmed(recovery,session)){MessageBox.Show(this,"恢复自动尚未确认。独立看护仍会继续尝试，请勿结束所有进程；请保留日志并检查系统。","恢复未确认",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                    }
                }
            }finally{timer.Stop();tray.Visible=false;tray.Dispose();if(worker!=null)worker.Dispose();}
        }
        public void SavePreview(string path){
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();
            Text="Redmi Fan Control · 界面预览（示例数据）";
            ShowFrame(new Frame{Utc=DateTime.UtcNow.ToString("o"),Connected=true,CanControl=true,Manual=false,Rpm1=2652,Rpm2=2671,Output1=30.5,Output2=34.4,CpuC=42.5,Status="界面预览 · 示例数据，未连接硬件"});
            note.Text="界面预览：上述为示例数据，不是实测值。此模式不读取或写入硬件。";
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Hide();
        }
        internal void VerifyUiContract(){
            if(!preview)throw new InvalidOperationException("UI contract test must not use real hardware.");
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();
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
            if(apply.Text!="应用温度曲线" || !apply.Enabled || !stateHeading.Text.Contains("尚未应用"))throw new Exception("Draft state");
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
            Hide();
        }
    }
}
