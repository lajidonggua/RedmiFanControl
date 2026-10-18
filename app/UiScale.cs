using System;
using System.Drawing;
using System.Windows.Forms;
namespace RedmiFanControl {
    static class UiScale {
        // Layout coordinates are authored at 96 DPI. Scale once after every child
        // exists; system-aware fonts already render at the current device DPI.
        public static void Apply(Form form,Size designSize,Size minimum){
            float factor;using(var g=form.CreateGraphics())factor=g.DpiX/96f;
            form.SuspendLayout();form.AutoScaleMode=AutoScaleMode.None;
            if(Math.Abs(factor-1)>0.01f)foreach(Control control in form.Controls)control.Scale(new SizeF(factor,factor));
            Rectangle area=Screen.FromControl(form).WorkingArea;
            int maxWidth=Math.Max(400,area.Width-40),maxHeight=Math.Max(400,area.Height-90);
            form.MinimumSize=new Size(Math.Min((int)(minimum.Width*factor),maxWidth),Math.Min((int)(minimum.Height*factor),maxHeight));
            form.ClientSize=new Size(Math.Min((int)(designSize.Width*factor),maxWidth),Math.Min((int)(designSize.Height*factor),maxHeight));
            ScaleGridRows(form,factor);form.ResumeLayout(true);
        }
        static void ScaleGridRows(Control parent,float factor){
            foreach(Control c in parent.Controls){
                var grid=c as DataGridView;
                if(grid!=null){grid.ColumnHeadersHeight=(int)Math.Ceiling(grid.ColumnHeadersHeight*factor);foreach(DataGridViewRow row in grid.Rows)row.Height=(int)Math.Ceiling(row.Height*factor);grid.RowTemplate.Height=(int)Math.Ceiling(grid.RowTemplate.Height*factor);}
                ScaleGridRows(c,factor);
            }
        }
    }
}
