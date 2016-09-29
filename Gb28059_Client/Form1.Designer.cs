namespace Gb28059_Client
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.lBoxDevice = new System.Windows.Forms.ListBox();
            this.btnXML = new System.Windows.Forms.Button();
            this.btnReal = new System.Windows.Forms.Button();
            this.btnQuery = new System.Windows.Forms.Button();
            this.lblInit = new System.Windows.Forms.Label();
            this.btnDevice = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.txtLocalIP = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtRemoteIP = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(12, 12);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(75, 23);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // btnStop
            // 
            this.btnStop.Location = new System.Drawing.Point(111, 12);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(75, 23);
            this.btnStop.TabIndex = 1;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // lBoxDevice
            // 
            this.lBoxDevice.FormattingEnabled = true;
            this.lBoxDevice.ItemHeight = 12;
            this.lBoxDevice.Location = new System.Drawing.Point(6, 21);
            this.lBoxDevice.Name = "lBoxDevice";
            this.lBoxDevice.Size = new System.Drawing.Size(168, 172);
            this.lBoxDevice.TabIndex = 4;
            this.lBoxDevice.SelectedIndexChanged += new System.EventHandler(this.lBoxDevice_SelectedIndexChanged);
            // 
            // btnXML
            // 
            this.btnXML.Location = new System.Drawing.Point(317, 12);
            this.btnXML.Name = "btnXML";
            this.btnXML.Size = new System.Drawing.Size(75, 23);
            this.btnXML.TabIndex = 5;
            this.btnXML.Text = "Test";
            this.btnXML.UseVisualStyleBackColor = true;
            this.btnXML.Click += new System.EventHandler(this.btnXML_Click);
            // 
            // btnReal
            // 
            this.btnReal.Location = new System.Drawing.Point(317, 129);
            this.btnReal.Name = "btnReal";
            this.btnReal.Size = new System.Drawing.Size(75, 23);
            this.btnReal.TabIndex = 6;
            this.btnReal.Text = "Real";
            this.btnReal.UseVisualStyleBackColor = true;
            this.btnReal.Click += new System.EventHandler(this.btnReal_Click);
            // 
            // btnQuery
            // 
            this.btnQuery.Location = new System.Drawing.Point(317, 51);
            this.btnQuery.Name = "btnQuery";
            this.btnQuery.Size = new System.Drawing.Size(75, 23);
            this.btnQuery.TabIndex = 8;
            this.btnQuery.Text = "Query";
            this.btnQuery.UseVisualStyleBackColor = true;
            this.btnQuery.Click += new System.EventHandler(this.btnQuery_Click);
            // 
            // lblInit
            // 
            this.lblInit.AutoSize = true;
            this.lblInit.ForeColor = System.Drawing.Color.Red;
            this.lblInit.Location = new System.Drawing.Point(91, 62);
            this.lblInit.Name = "lblInit";
            this.lblInit.Size = new System.Drawing.Size(83, 12);
            this.lblInit.TabIndex = 9;
            this.lblInit.Text = "sip服务未启动";
            // 
            // btnDevice
            // 
            this.btnDevice.Location = new System.Drawing.Point(317, 90);
            this.btnDevice.Name = "btnDevice";
            this.btnDevice.Size = new System.Drawing.Size(75, 23);
            this.btnDevice.TabIndex = 10;
            this.btnDevice.Text = "QueryDev";
            this.btnDevice.UseVisualStyleBackColor = true;
            this.btnDevice.Click += new System.EventHandler(this.btnDevice_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(317, 168);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "Bye";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // txtLocalIP
            // 
            this.txtLocalIP.Location = new System.Drawing.Point(262, 207);
            this.txtLocalIP.Name = "txtLocalIP";
            this.txtLocalIP.Size = new System.Drawing.Size(130, 21);
            this.txtLocalIP.TabIndex = 12;
            this.txtLocalIP.Text = "192.168.10.146:21000";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(201, 211);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(47, 12);
            this.label3.TabIndex = 13;
            this.label3.Text = "LocalIP";
            // 
            // txtRemoteIP
            // 
            this.txtRemoteIP.Location = new System.Drawing.Point(262, 244);
            this.txtRemoteIP.Name = "txtRemoteIP";
            this.txtRemoteIP.Size = new System.Drawing.Size(130, 21);
            this.txtRemoteIP.TabIndex = 12;
            this.txtRemoteIP.Text = "192.168.10.250:5000";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(201, 249);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(53, 12);
            this.label4.TabIndex = 13;
            this.label4.Text = "RemoteIP";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(14, 62);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(65, 12);
            this.label1.TabIndex = 14;
            this.label1.Text = "消息状态：";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.lBoxDevice);
            this.groupBox1.Location = new System.Drawing.Point(16, 108);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(180, 204);
            this.groupBox1.TabIndex = 15;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "设备列表";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(418, 318);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.txtRemoteIP);
            this.Controls.Add(this.txtLocalIP);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.lblInit);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.btnDevice);
            this.Controls.Add(this.btnQuery);
            this.Controls.Add(this.btnReal);
            this.Controls.Add(this.btnXML);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.Text = "GB28059交通厅监控对接";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.groupBox1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.ListBox lBoxDevice;
        private System.Windows.Forms.Button btnXML;
        private System.Windows.Forms.Button btnReal;
        private System.Windows.Forms.Button btnQuery;
        private System.Windows.Forms.Label lblInit;
        private System.Windows.Forms.Button btnDevice;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.TextBox txtLocalIP;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtRemoteIP;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.GroupBox groupBox1;
    }
}

