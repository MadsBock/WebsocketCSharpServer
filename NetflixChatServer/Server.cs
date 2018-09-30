using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace NetflixChatServer
{
    class Server : TcpListener
    {

        List<TcpClient> connectedClients = new List<TcpClient>();

        public Server(int port) : base(new IPEndPoint(IPAddress.Any, port))
        {
            Start();
            ListenForClients();
        }

        public async void ListenForClients()
        {
            while (true)
            {
                var client = await AcceptTcpClientAsync();
                Console.WriteLine("Client Connected!");
                Handshake(client);
            }
            
        }

        public async void Handshake(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[client.ReceiveBufferSize];
            var charactersRead = await stream.ReadAsync(buffer, 0, client.Available);
            var text = Encoding.UTF8.GetString(buffer, 0, charactersRead);
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^GET"))
            {
                var response = GetHandshakeReponse(text);
                var dataToSend = Encoding.UTF8.GetBytes(response);
                stream.Write(dataToSend, 0, dataToSend.Length);
                connectedClients.Add(client);
                ReceiveMessages(client);
            } else
            {
                client.Close();
            }
        }

        public async void ReceiveMessages(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[client.ReceiveBufferSize];
            int read = 0;
            while(true)
            {
                try
                {
                    read = await stream.ReadAsync(buffer, 0, client.Available);
                }
                catch (ObjectDisposedException) {
                    CloseClient(client);
                }
                if (read == 0) continue;
                //Extract Opcode
                var opcode = buffer[0] & 15;

                //Data Frame or Control Frame?
                var dataframe = opcode < 8;

                if(dataframe)
                {
                    //What type of data is it?
                    var isText = opcode == 1;
                    int keystart = 2;
                    
                    ulong length = (ulong)(buffer[1] - 128);
                    if (buffer[1] <0)
                    {
                        CloseClient(client);
                        return;
                    }                  
                    
                    if(length == 126)
                    {
                        length = (ulong)BitConverter.ToUInt16(new byte[] { buffer[3], buffer[2] }, 0);
                        keystart = 4;
                    } else if(length == 127)
                    {
                        length = (ulong)BitConverter.ToUInt64(new byte[] { buffer[9], buffer[8], buffer[7], buffer[6], buffer[5], buffer[4], buffer[3], buffer[2] }, 2);
                        keystart = 10;
                    }

                    var keys = new byte[4];
                    Array.Copy(buffer, keystart, keys, 0, 4);

                    var messageStart = keystart + 4;
                    for(ulong i = 0; i < length; i++)
                    {
                        var index = (ulong)messageStart + i;
                        var keyIndex = i % 4;
                        buffer[index] = (byte)(buffer[index] ^ keys[keyIndex]);
                    }

                    var message = "";
                    while(length > 0)
                    {
                        var currentLength = (int)Math.Min(int.MaxValue, length);
                        message += Encoding.UTF8.GetString(buffer, messageStart, currentLength);
                        length -= (ulong)currentLength;
                        messageStart += currentLength;
                    }
                    BroadcastMessage(message);
                } else
                {
                    if(opcode == 8)
                    {
                        CloseClient(client);
                    }
                }
            }
        }

        private void BroadcastMessage(string message)
        {
            foreach(var client in connectedClients.ToArray())
            {
                if (client.Connected)
                {
                    SendMessage(message, client.GetStream());
                } else
                {
                    CloseClient(client);

                }
            }
        }

        
        //TODO make sure this function will only ever be called on its own
        private async void SendMessage(string message, NetworkStream stream)
        {
            var list = new List<byte>();
            var bytesToSend = Encoding.UTF8.GetBytes(message);
            var l = bytesToSend.Length;

            //Opcode      
            list.Add(129);

            //Length
            if(l <= 125)
            {
                list.Add((byte)l);
            } else if (l <= ushort.MaxValue)
            {
                var lBytes = BitConverter.GetBytes((ushort)l);
                list.Add(126);
                list.Add(lBytes[1]);
                list.Add(lBytes[0]);
            } else
            {
                var lBytes = BitConverter.GetBytes((ulong)l);
                list.Add(127);
                for (int i = 7; i >= 0; i--)
                {
                    list.Add(lBytes[i]);
                }
            }

            //Pack the data into a chunk of the proper size
            byte[] chunk = new byte[l];
            Array.Copy(bytesToSend, 0, chunk, 0, l);
            list.AddRange(chunk);

            //Send the frame
            var buffer = list.ToArray();
            await stream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void CloseClient(TcpClient client)
        {
            client.Close();
            connectedClients.Remove(client);
        }

        public string GetHandshakeReponse(string data)
        {
            const string eol = "\r\n";
            var key = new System.Text.RegularExpressions.Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            var hash = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(key));
            string acceptKey = Convert.ToBase64String(hash);

            return "HTTP/1.1 101 Switching Protocols" + eol
                + "Connection: Upgrade" + eol
                + "Upgrade: websocket" + eol
                + "Sec-WebSocket-Accept: " + acceptKey + eol
                + eol;
        }
    }
}
