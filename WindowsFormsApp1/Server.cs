using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Server : Form
    {
        private bool active = false;

        // Thread responsible for starting TcpListener;
        private Thread listener = null;
        private long id = 1;
        // Client object
        private struct NewClient
        {
            public long id;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        }

        // List of connected clients
        private ConcurrentDictionary<long, NewClient> list = new ConcurrentDictionary<long, NewClient>();

        // Async operation
        private Task send = null;

        private bool exit = false;

        public Server()
        {
            InitializeComponent();
        }

        private void WriteToLog(string msg = null)
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
                        richTextBox1.AppendText(DateTime.Now.ToString("HH:mm") + " " + msg);
                    }
                });
            }
        }

        private void StartListening(IPAddress iPAddress, int port)
        {
            TcpListener listener = null;

            try
            {
                WriteToLog("Server Started. Waiting for connections....");
                listener = new TcpListener(iPAddress, port);
                listener.Start();
                Activate(true);

                while (active)
                {
                    if (listener.Pending())
                    {
                        try
                        {
                            // Create client for pending listener
                            NewClient obj = new NewClient();
                            obj.id = id;
                            obj.client = listener.AcceptTcpClient();
                            obj.buffer = new byte[obj.client.ReceiveBufferSize];
                            obj.stream = obj.client.GetStream();
                            obj.data = new StringBuilder();
                            obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);

                            Thread th = new Thread(() => NewConnection(obj));
                            th.IsBackground = true;
                            th.Start();
                            id++;
                        }
                        catch (Exception e)
                        {
                            WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Something went wrong: {0}", e.Message);
            }
            finally
            {
                if (listener != null)
                {
                    listener.Server.Close();
                }
            }
        }

        private void Activate(bool status)
        {
            if (!exit)
            {
                connectBtn.Invoke((MethodInvoker)delegate
               {
                   active = status;
                   if (status)
                   {
                       connectBtn.Text = "Disconnect";
                       WriteToLog("Server has started....");
                   }
                   else
                   {
                       connectBtn.Text = "Connect";
                       WriteToLog("Server has stopped...");
                   }
               });
            }
        }

        private void ConnectBtn_click(object sender, EventArgs eventArgs)
        {
            if (active)
            {
                // Disconnect if connected;
                Activate(false);
            }
            else if (listener == null || !listener.IsAlive)
            {
                string ip = ipAddressBox.Text;
                string port = portBox.Text;
                IPAddress localaddress = IPAddress.Parse(ip);

                listener = new Thread(() => StartListening(localaddress, int.Parse(port)));
                listener.IsBackground = true;
                listener.Start();
            }
        }

        private void NewConnection(NewClient obj)
        {
            // add to list of clients;
            list.TryAdd(obj.id, obj);
            string msg = string.Format("Client {0} connected", obj.id);

            WriteToLog(msg);
            SendTask(msg, obj.id);

            while (obj.client.Connected)
            {
                try
                {
                    // while the client is connected, constantly read from the buffer asynchronously
                    obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                    // Block thread until otherwise noted;
                    obj.handle.WaitOne();
                }
                catch (Exception e)
                {
                    WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                }
            }
            // Client is no longer connected
            obj.client.Close();
            msg = string.Format("Client {0} disconnected", obj.id);
            WriteToLog(msg);
            SendTask(msg, obj.id);
            list.TryRemove(obj.id, out NewClient tmp);
        }

        private void Read(IAsyncResult result)
        {
            NewClient obj = (NewClient)result.AsyncState;
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    WriteToLog(string.Format("[/ {0} /]", ex.Message));
                }
            }
            if (bytes > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                    }
                    else
                    {
                        string msg = string.Format("Client {0}: {1}", obj.id, obj.data);
                        WriteToLog(msg);
                        SendTask(msg, obj.id);
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    WriteToLog(string.Format("[/ {0} /]", ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void Send(string msg, long id = -1)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            // Send msg to all clients in `list`
            foreach (KeyValuePair<long, NewClient> obj in list)
            {
                // write to all other clients, besides YOU
                if (id != obj.Value.id && obj.Value.client.Connected)
                {
                    try
                    {
                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                    }
                    catch (Exception e)
                    {
                        WriteToLog(string.Format("Something went wrong: {0}", e.Message));
                    }
                }
            }
        }

        private void SendTask(string msg, long id = -1)
        {
            if (send == null || send.IsCompleted)
            {
                // Task represents an async operation
                send = Task.Factory.StartNew(() => Send(msg, id));
            }
            else
            {
                send.ContinueWith(task => Send(msg, id));
            }
        }

        private void Write(IAsyncResult ar)
        {
            NewClient obj = (NewClient)ar.AsyncState;
            if (obj.client.Connected)
            {
                try
                {
                    // Completes the async call to `BeginWrite`
                    obj.stream.EndWrite(ar);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Something went wrong: {0}", e.Message);
                }
            }
        }

        private void SendBtn_Click(object sender, EventArgs eventArgs)
        {
            if (sendInput.Text.Length > 0)
            {
                string msg = sendInput.Text;
                sendInput.Clear();
                WriteToLog("Server (You): " + msg);
                SendTask("Server: " + msg);
            }
        }

    }
}
