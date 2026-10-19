using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        Panel percentPanel,rpmPanel,curvePanel;CurveGraph curvePreview;
        Label fanHint;Panel canvas;
        void InitializeLayout(){
            SuspendLayout();AutoScaleMode=AutoScaleMode.None;
            Text="Redmi Fan Control · "+Application.ProductVersion;
            ClientSize=new Size(920,700);MinimumSize=new Size(760,520);
            StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9);
            BackColor=Color.FromArgb(246,248,251);Icon=AppBrand.WindowIcon();
            viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};Controls.Add(viewport);
            canvas=new Panel{Size=new Size(920,700),Location=Point.Empty};viewport.Controls.Add(canvas);
            var header=new Panel{Location=Point.Empty,Size=new Size(920,76),BackColor=Color.White};canvas.Controls.Add(header);
            var logo=new PictureBox{Image=AppBrand.HeaderImage(),Location=new Point(20,16),Size=new Size(42,42),SizeMode=PictureBoxSizeMode.Zoom};header.Controls.Add(logo);
            AddLabel(header,"Redmi Fan Control",74,10,520,30,18,Ink,true);
            AddLabel(header,"Redmi Book Pro 15 2022 · Ryzen 7 6800H · 双风扇联动",76,46,600,21,9,Muted,false);
            mode=AddLabel(header,"等待连接",690,14,210,24,10,Muted,true);mode.TextAlign=ContentAlignment.MiddleRight;
            InitializeAutostart(header);
            statePanel=new Panel{Location=new Point(20,88),Size=new Size(880,74),BackColor=Color.FromArgb(231,240,253)};canvas.Controls.Add(statePanel);
            stateHeading=AddLabel(statePanel,"正在连接硬件",14,8,850,25,11,Ink,true);
            note=AddLabel(statePanel,"连接后可查看温度与转速。",14,35,850,34,9,Muted,false);
            var left=fanSection=Section("风扇实时状态",20,176,530,160);
            fans=Grid(left,12,40,506,82,new string[]{"风扇","当前转速","实际输出","状态"},new float[]{17,29,22,32});
            fans.Rows.Add("风扇 1","--","--","等待数据");fans.Rows.Add("风扇 2","--","--","等待数据");
            fanHint=AddLabel(left,"等待实时数据；读取失败显示 --。",16,128,496,22,8,Muted,false);
            var right=temperatureSection=Section("CPU 温度",566,176,334,160);
            cpuBadge=AddLabel(right,"-- °C",20,40,290,45,26,Blue,true);
            sensors=Grid(right,14,92,306,55,new string[]{"传感器","本次最低 / 最高"},new float[]{46,54});
            sensors.Rows.Add("Tctl/Tdie","--");
            var control=controlSection=Section("自定义调速",20,350,560,242);
            AddLabel(control,"控制方式",18,45,90,26,9,Muted,false);
            controlMode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(112,40),Size=new Size(425,29)};
            controlMode.Items.AddRange(new object[]{"固定百分比 · 0–100%","目标转速 · RPM","温度曲线 · 随 CPU 温度变化"});controlMode.SelectedIndex=2;control.Controls.Add(controlMode);
            percentPanel=new Panel{Location=new Point(16,78),Size=new Size(526,116)};control.Controls.Add(percentPanel);
            slider=new TrackBar{Minimum=0,Maximum=100,Value=60,TickFrequency=10,SmallChange=1,LargeChange=5,Location=new Point(0,4),Size=new Size(360,46)};percentPanel.Controls.Add(slider);
            number=new NumericUpDown{Minimum=0,Maximum=100,Value=60,Location=new Point(377,12),Size=new Size(88,28)};percentPanel.Controls.Add(number);
            var percentUnit=AddLabel(percentPanel,"%",474,15,30,23,10,Muted,false);
            AddLabel(percentPanel,"快捷预设",8,65,80,24,9,Muted,false);
            presets=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(96,60),Size=new Size(181,28)};
            foreach(int value in PresetValues)presets.Items.Add(value==0?"0% · 请求停转":value+"%" );presets.SelectedIndex=4;percentPanel.Controls.Add(presets);
            var percentHint=AddLabel(percentPanel,"实际输出受右侧规则约束",294,65,225,25,8,Muted,false);
            rpmPanel=new Panel{Location=percentPanel.Location,Size=percentPanel.Size};control.Controls.Add(rpmPanel);
            AddLabel(rpmPanel,"目标转速",8,14,100,26,11,Ink,true);
            rpmTarget=new NumericUpDown{Minimum=0,Maximum=6000,Increment=100,Value=3000,Location=new Point(118,11),Size=new Size(130,30)};rpmPanel.Controls.Add(rpmTarget);
            AddLabel(rpmPanel,"RPM",260,15,60,23,10,Muted,false);
            AddLabel(rpmPanel,"0 表示请求停转；非零目标为 1000–6000 RPM。\n两路联动，以转得较慢的一路为准。",8,59,504,51,9,Muted,false);
            curvePanel=new Panel{Location=new Point(16,77),Size=new Size(526,119)};control.Controls.Add(curvePanel);
            curvePreview=new CurveGraph{Location=Point.Empty,Size=new Size(322,119),Compact=true};curvePanel.Controls.Add(curvePreview);
            curveText=new TextBox{Visible=false,Text=CurveFormat.Format(new UserConfiguration().Curve)};control.Controls.Add(curveText);
            InitializeConfigurationUi(curvePanel);
            validation=AddLabel(control,"",18,203,520,28,8,Muted,false);
            var rules=rulesSection=Section("静音与温度规则",596,350,304,242);InitializeStopRules(rules);
            var footer=new Panel{Location=new Point(20,606),Size=new Size(880,48),BackColor=Color.White};canvas.Controls.Add(footer);
            configStatus=AddLabel(footer,"尚未保存配置",12,8,310,32,8,Muted,false);
            saveConfig=Button(footer,"仅保存配置",330,6,135,36,false);saveConfig.Click+=delegate{SaveConfigurationUi();};
            apply=Button(footer,"保存并应用曲线",479,6,188,36,true);automatic=Button(footer,"恢复系统自动",681,6,187,36,false);
            hints.SetToolTip(saveConfig,"只记住配置，不改变当前风扇。点击“保存并应用”才会生效。");
            status=AddLabel(canvas,"正在准备监测…",22,667,365,24,8,Muted,false);
            updated=AddLabel(canvas,"",392,667,120,24,8,Muted,false);
            var data=Button(canvas,"设置 / 关于",516,661,111,29,false);data.Click+=delegate{ShowSettings();};
            reconnect=Button(canvas,"连接硬件",641,661,126,29,false);exit=Button(canvas,"退出",781,661,119,29,false);
            Disposed+=delegate{logo.Image.Dispose();Icon.Dispose();hints.Dispose();timer.Dispose();tray.Dispose();};
            ResumeLayout(false);UiScale.Apply(this,new Size(920,700),new Size(760,520));
            // Apply anchors only after DPI scaling, so parent resizing cannot scale offsets twice.
            AnchorMany(AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,header,statePanel,stateHeading,note,fans,fanHint,sensors,controlMode,percentPanel,rpmPanel,slider);
            AnchorMany(AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,cpuBadge,left.Controls[0],right.Controls[0],control.Controls[0],rules.Controls[0]);
            AnchorMany(AnchorStyles.Top|AnchorStyles.Right,mode,startupBox,number,percentUnit,percentHint,editCurve,curveSummary);
            AnchorMany(AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,curvePanel,curvePreview);
            AnchorMany(AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,validation,footer,status);
            AnchorMany(AnchorStyles.Bottom|AnchorStyles.Right,updated,data,reconnect,exit);
            AnchorMany(AnchorStyles.Top|AnchorStyles.Right,saveConfig,apply,automatic);
            AnchorMany(AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,configStatus);
            InitializeResponsiveLayout();
        }
        void ShowSettings(){
            using(var dialog=new Form{Text="设置与关于",Icon=AppBrand.WindowIcon(),ClientSize=new Size(570,335),Font=Font,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
                dialog.SuspendLayout();
                AddLabel(dialog,"Redmi Fan Control  "+Application.ProductVersion,20,18,530,30,14,Ink,true);
                AddLabel(dialog,"双击 EXE 即可启动。风扇读写需要官方 PawnIO 驱动。\n最小化和隐藏会留在托盘；选择退出时才会恢复系统自动。",20,59,530,55,9,Muted,false);
                AddLabel(dialog,"配置、日志、内嵌模块及第三方许可证保存在：\n"+AppPaths.DataRoot,20,121,530,66,9,Ink,false);
                AddLabel(dialog,"点击 × 的行为",20,198,135,25,9,Muted,false);
                var closeBehavior=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(158,194),Size=new Size(390,29)};
                closeBehavior.Items.AddRange(new object[]{CloseBehaviorText(CloseBehavior.Ask),CloseBehaviorText(CloseBehavior.HideToTray),CloseBehaviorText(CloseBehavior.Exit)});
                closeBehavior.SelectedIndex=uiPreferences==null?(int)CloseBehavior.Ask:(int)uiPreferences.CloseBehavior;dialog.Controls.Add(closeBehavior);
                AddLabel(dialog,"选择后立即保存；也可以恢复为每次询问。",158,226,390,22,8,Muted,false);
                closeBehavior.SelectedIndexChanged+=delegate{
                    if(uiPreferences==null)return;uiPreferences.CloseBehavior=(CloseBehavior)closeBehavior.SelectedIndex;SaveUiPreferences();
                };
                var open=Button(dialog,"打开数据文件夹",20,278,164,36,false);
                open.Click+=delegate{if(preview)return;try{RuntimeModules.Prepare(AppPaths.Modules);DirectoryOpen(AppPaths.DataRoot);}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"无法打开数据目录");}};
                var driver=Button(dialog,"PawnIO 官方网站",198,278,174,36,false);
                driver.Click+=delegate{if(!preview)Process.Start(new ProcessStartInfo("https://pawnio.eu/"){UseShellExecute=true});};
                var done=Button(dialog,"关闭",390,278,158,36,true);done.Click+=delegate{dialog.Close();};
                dialog.Disposed+=delegate{dialog.Icon.Dispose();};dialog.ResumeLayout(false);UiScale.Apply(dialog,new Size(570,335),new Size(570,335));dialog.ShowDialog(this);
            }
        }
        static void DirectoryOpen(string directory){System.IO.Directory.CreateDirectory(directory);Process.Start(new ProcessStartInfo(directory){UseShellExecute=true});}
    }
}
