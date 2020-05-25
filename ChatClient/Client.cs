using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatClient
{
    public partial class Client : Form
    {
        private bool connected = false;
        private Thread client = null;
        private struct NewClient
        {
            public NetworkStream stream;
            public TcpClient client;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        }

        private bool exit = false;
        private Task send = null;
        private NewClient obj;
        public Client()
        {
            InitializeComponent();
        }

        public void WriteToLog(string msg = null)
        {
            if (!exit)
            {
                richTextBox1.Invoke((MethodInvoker)delegate
                {
                    if (msg == null)
                    {
                        richTextBox1.Clear();
                    }
                    else
                    {
                        if (richTextBox1.Text.Length > 0)
                        {
                            richTextBox1.AppendText(Environment.NewLine);
                        }
                        richTextBox1.AppendText(string.Format("{0} {1}", DateTime.Now.ToString("HH:mm"), msg));
                    }
                });
            }
        }

        public void ConnectBtn_Click(object sender, EventArgs eventArgs)
        {
            // If already connected, disconnect, set ConnectBtn text to "Disconnected"
            if (connected)
            {
                //SetConnectBtn(false);
                obj.client.Close();
            }
            // Client has not connected. Initialize new client connection on new thread
            else if (client == null || !client.IsAlive)
            {
                bool localaddrResult = IPAddress.TryParse(ipInput.Text, out IPAddress localaddr);
                if (!localaddrResult)
                {
                    WriteToLog("[/ Address is not valid /]");
                }

                bool portResult = int.TryParse(portInput.Text, out int port);
                if (!portResult)
                {
                    WriteToLog("[/ Port number is not valid /]");
                }
                else if (port < 0 || port > 65535)
                {
                    portResult = false;
                    WriteToLog("[/ Port number is out of range /]");
                }
                if (localaddrResult && portResult)
                {
                    client = new Thread(() => ConnectToServer(localaddr, port))
                    {
                        IsBackground = true
                    };
                    client.Start();
                }
            }
        }

        private void ConnectToServer(IPAddress localaddress, int port)
        {
            try
            {
                obj = new NewClient();
                obj.client = new TcpClient();
                obj.client.Connect(localaddress, port);
                obj.stream = obj.client.GetStream();
                obj.buffer = new byte[obj.client.ReceiveBufferSize];
                obj.data = new StringBuilder();
                obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                SetConnectBtn(true);

                while (obj.client.Connected)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                        obj.handle.WaitOne();
                    }
                    catch(Exception e)
                    {
                        WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                    }
                }

                obj.client.Close();
                SetConnectBtn(false);
            }
            catch(Exception e)
            {
                WriteToLog(string.Format("Something went wrong: {0}", e.Message));
            }
        }

        private void Read(IAsyncResult ar)
        {
            int bytesRead = 0;
            
            if (obj.client.Connected)
            {
                try
                {
                    bytesRead = obj.stream.EndRead(ar);
                }
                catch(Exception e)
                {
                    WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                }
            }

            if(bytesRead > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytesRead));
                try
                {
                    // Is there data to be read?
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                    }
                    else
                    {
                        WriteToLog(obj.data.ToString());
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch(Exception e)
                {
                    obj.data.Clear();
                    WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }

        }

        private void Write(IAsyncResult asyncResult)
        {
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(asyncResult);
                }
                catch(Exception e)
                {
                    WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                }
            }
        }

        private void SendTask(string msg)
        {
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => Send(msg));
            }
            else
            {
                send.ContinueWith(task => Send(msg));
            }
        }

        private void Send(string msg)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);

            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), null);
                }
                catch(Exception e)
                {
                    WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                }
            }
        }

        private void SetConnectBtn(bool status)
        {
            if (!exit)
            {
                connectBtn.Invoke((MethodInvoker)delegate
                {
                    connected = status;
                    if (status)
                    {
                        connectBtn.Text = "Disconnected";
                        WriteToLog("You are connected to the server...");
                    } 
                    else
                    {
                        connectBtn.Text = "Connected";
                        WriteToLog("You have disconnected from the server...");
                    }
                });
            }
        }

        private void SendBtn_Click(object sender, EventArgs eventArgs)
        {
            if (sendInput.Text.Length > 0)
            {
                string msg = sendInput.Text;
                sendInput.Clear();
                WriteToLog(string.Format("You: {0}", msg));

                if (connected)
                {
                    SendTask(msg);
                }
            }
        }

        private void SendInput_KeyDown(object sender, KeyEventArgs keyEvent)
        {
            if (keyEvent.KeyCode == Keys.Enter)
                sendBtn.PerformClick();
        }
    }
}
