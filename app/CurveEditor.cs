using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace RedmiFanControl {
    sealed class CurveGraph : Control {
        public CurvePoint[] Points;
        public bool Compact;
        public CurveGraph(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=g.DpiX/96f;
            int left=(int)(38*scale),top=(int)((Compact?10:30)*scale),w=Width-(int)(52*scale),h=Height-(int)((Compact?39:72)*scale);if(w<10 || h<10)return;
            using(var pen=new Pen(Color.FromArgb(228,233,240))){
                for(int i=0;i<=5;i++){int y=top+h-i*h/5;g.DrawLine(pen,left,y,left+w,y);g.DrawString((i*20)+"%",Font,Brushes.DimGray,3*scale,y-7*scale);}
                for(int t=20;t<=80;t+=10){int x=left+(t-20)*w/60;g.DrawLine(pen,x,top,x,top+h);g.DrawString(t+"°",Font,Brushes.DimGray,x-10*scale,top+h+8*scale);}
            }
            if(!Compact)g.DrawString("请求输出曲线（实际输出受静音与温度规则约束）",Font,Brushes.DimGray,left,4);
            if(Points==null || Points.Length<2)return;
            var plot=new List<PointF>();
            plot.Add(new PointF(left,top+h-Points[0].Percent*h/100f));
            foreach(var p in Points)plot.Add(new PointF(left+(p.Temperature-20)*w/60f,top+h-p.Percent*h/100f));
            plot.Add(new PointF(left+w,top+h-Points[Points.Length-1].Percent*h/100f));
            using(var line=new Pen(Color.FromArgb(33,103,200),3*scale))g.DrawLines(line,plot.ToArray());
            foreach(var p in Points){float x=left+(p.Temperature-20)*w/60f,y=top+h-p.Percent*h/100f;g.FillEllipse(Brushes.Teal,x-4*scale,y-4*scale,8*scale,8*scale);}
        }
    }
    sealed class CurveEditor : Form {
        readonly DataGridView table;readonly CurveGraph graph;readonly Label feedback;readonly Button done;
        public CurvePoint[] Result;
        public CurveEditor(CurvePoint[] points){
            SuspendLayout();AutoScaleMode=AutoScaleMode.None;
            Text="编辑温度曲线";Icon=AppBrand.WindowIcon();ClientSize=new Size(800,460);Font=new Font("Microsoft YaHei UI",9);
            StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            Controls.Add(new Label{Text="逐行填写温度和输出，不用输入冒号或分号。0% 表示请求停转。",Location=new Point(18,14),Size=new Size(760,28)});
            table=new DataGridView{Location=new Point(18,50),Size=new Size(280,294),AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.CellSelect,MultiSelect=false};
            table.Columns.Add("temperature","温度 °C");table.Columns.Add("output","输出 %");foreach(DataGridViewColumn c in table.Columns)c.SortMode=DataGridViewColumnSortMode.NotSortable;
            table.RowTemplate.Height=32;Controls.Add(table);
            graph=new CurveGraph{Location=new Point(316,50),Size=new Size(466,294)};Controls.Add(graph);
            var add=new Button{Text="添加节点",Location=new Point(18,354),Size=new Size(88,30)};Controls.Add(add);
            var remove=new Button{Text="删除节点",Location=new Point(114,354),Size=new Size(88,30)};Controls.Add(remove);
            var sort=new Button{Text="按温度排序",Location=new Point(210,354),Size=new Size(100,30)};Controls.Add(sort);
            feedback=new Label{Location=new Point(18,393),Size=new Size(560,53),ForeColor=Color.Firebrick};Controls.Add(feedback);
            done=new Button{Text="使用此曲线",Location=new Point(584,403),Size=new Size(106,34)};Controls.Add(done);
            var cancel=new Button{Text="取消",Location=new Point(704,403),Size=new Size(78,34),DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
            foreach(var p in points)table.Rows.Add(p.Temperature,p.Percent);
            table.CellValueChanged+=delegate{ValidateDraft();};table.CurrentCellDirtyStateChanged+=delegate{if(table.IsCurrentCellDirty)table.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            table.DataError+=delegate(object sender,DataGridViewDataErrorEventArgs e){e.ThrowException=false;ValidateDraft();};
            add.Click+=delegate{if(table.Rows.Count>=12)return;table.Rows.Add(null,null);table.CurrentCell=table.Rows[table.Rows.Count-1].Cells[0];ValidateDraft();};
            remove.Click+=delegate{if(table.CurrentCell!=null)table.Rows.RemoveAt(table.CurrentCell.RowIndex);ValidateDraft();};
            sort.Click+=delegate{try{var p=ReadRows();Array.Sort(p,delegate(CurvePoint a,CurvePoint b){return a.Temperature.CompareTo(b.Temperature);});table.Rows.Clear();foreach(var point in p)table.Rows.Add(point.Temperature,point.Percent);}catch(Exception ex){feedback.Text=ex.Message;}ValidateDraft();};
            done.Click+=delegate{try{table.EndEdit();Result=ReadRows();ControlPolicy.ValidateCurve(Result);DialogResult=DialogResult.OK;Close();}catch(Exception ex){feedback.Text=ex.Message;}};
            Disposed+=delegate{Icon.Dispose();};ValidateDraft();ResumeLayout(false);UiScale.Apply(this,new Size(800,460),new Size(800,460));
        }
        CurvePoint[] ReadRows(){
            var p=new List<CurvePoint>();
            foreach(DataGridViewRow row in table.Rows){int t,v;if(!int.TryParse(Convert.ToString(row.Cells[0].Value),out t) || !int.TryParse(Convert.ToString(row.Cells[1].Value),out v))throw new ArgumentException("第 "+(row.Index+1)+" 行请填写整数温度与输出。");p.Add(new CurvePoint{Temperature=t,Percent=v});}
            return p.ToArray();
        }
        void ValidateDraft(){
            if(done==null)return;
            try{var p=ReadRows();ControlPolicy.ValidateCurve(p);graph.Points=p;feedback.Text="✓ 曲线有效。使用后仍需在主窗口点击应用。\n末节点须在 80°C 或以下达到 100%。";feedback.ForeColor=Color.SeaGreen;done.Enabled=true;}
            catch(Exception ex){graph.Points=null;feedback.Text=ex.Message;feedback.ForeColor=Color.Firebrick;done.Enabled=false;}
            graph.Invalidate();
        }
        internal void VerifyContract(){
            if(!done.Enabled)throw new Exception("Editor valid initial curve");
            table.Rows[1].Cells[1].Value="bad";ValidateDraft();if(done.Enabled || graph.Points!=null)throw new Exception("Editor invalid row");
            table.Rows[1].Cells[1].Value=10;ValidateDraft();if(!done.Enabled)throw new Exception("Editor corrected row");
            var p=ReadRows();if(p.Length!=6 || p[1].Percent!=10)throw new Exception("Editor node values");
        }
        internal void SavePreview(string path){
            Text="温度曲线编辑器 · 界面预览（未连接硬件）";ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-2500,-2500);Show();Application.DoEvents();
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Hide();
        }
    }
}
