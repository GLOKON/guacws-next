using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac.Parameters;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacConnection
    {
        private readonly WebSocketConnection webSocket;
        private readonly GuacDClient guacD;
        private readonly GuacOptions options;
        private readonly SymmetricCipher cipher;

        private int lastActivity;
        private bool handshakeReplySent;
        private ConnectionProfile connectionProfile;

        public GuacConnection(WebSocketConnection webSocket, GuacDClient guacD, GuacOptions options) {
            this.webSocket = webSocket;
            this.guacD = guacD;
            this.options = options;

            switch (options.Cipher.Type)
            {
                case CipherType.AES:
                    cipher = new SymmetricCipher(Aes.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.DES:
                    cipher = new SymmetricCipher(DES.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.RC2:
                    cipher = new SymmetricCipher(RC2.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.Rijndael:
                    cipher = new SymmetricCipher(Rijndael.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.TripleDES:
                    cipher = new SymmetricCipher(TripleDES.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
            }
        }

        public void Dispose()
        {
            guacD.Dispose();
            webSocket.Dispose();
        }

        public async Task StartAsync(HttpContext context)
        {
            connectionProfile = ParseToken(context.Request.Query)?.Connection;
            await guacD.ConnectAsync();
            webSocket.ReceiveText += HandleWebSocketMessage;
            guacD.ReceiveText += HandleGuacDMessage;
        }

        public async Task StopAsync()
        {
            webSocket.ReceiveText -= HandleWebSocketMessage;
            guacD.ReceiveText -= HandleGuacDMessage;
            await guacD.CloseAsync();
            await webSocket.CloseAsync();
        }

        private void HandleWebSocketMessage(object sender, string message)
        {
            // TODO: Log WS >> GUAC
        }

        private void HandleGuacDMessage(object sender, string message)
        {
            // TODO: Log GUAC >> WS
            string[] messageChunks = new string[] { message };

            foreach (string messageChunk in messageChunks)
            {
                if (handshakeReplySent)
                {
                    SendHandshakeReplyAsync(messageChunk);
                }
                else
                {

                }
            }
        }

        public async Task SendOpCodeAsync(string[] parameters)
        {
            string formattedOpCode = FormatOpCode(parameters);
            // TODO: Log op code
            await guacD.SendTextAsync(formattedOpCode, CancellationToken.None);
        }

        private async Task SendHandshakeReplyAsync(string handshake)
        {
            await SendOpCodeAsync(new string[] {
                "size",
                GetConnectionSetting("width"),
                GetConnectionSetting("height"),
                GetConnectionSetting("dpi")
            });

            await SendOpCodeAsync(new string[] { "audio", GetConnectionSetting("audio") });
            await SendOpCodeAsync(new string[] { "video", GetConnectionSetting("video") });
            await SendOpCodeAsync(new string[] { "image", GetConnectionSetting("image") });

            string[] parameterRequests = handshake.Split(',');
            IList<string> parameterReplies = new List<string>();

            foreach (string  parameterRequest in parameterRequests)
            {
                parameterReplies.Add(GetConnectionSetting(parameterRequest));
            }

            await SendOpCodeAsync(parameterReplies.ToArray());

            handshakeReplySent = true;
        }

        private string GetConnectionSetting(string parameter)
        {
            if (parameter.StartsWith("VERSION_"))
            {
                return connectionProfile.Settings.GetValueOrDefault("protocol_version", null);
            }

            return connectionProfile.Settings.GetValueOrDefault(parameter, null);
        }

        private string FormatOpCode(string[] opCodeParts)
        {
            string[] formattedOpCodes = opCodeParts.Select(opCodePart =>
            {
                string opCode = opCodePart ?? "";

                return string.Format("{0}.{1}", opCode.Length, opCode);
            }).ToArray();

            return string.Join(",", formattedOpCodes) + ";";
        }

        private GuacToken ParseToken(IQueryCollection query)
        {
            if (!query.TryGetValue("token", out StringValues values))
            {
                // TODO: Throw exception
            }

            string tokenValue = Encoding.UTF8.GetString(Convert.FromBase64String(values.ToString()));


            return null;
        }

        private async Task CheckActivityAsync()
        {
            if (lastActivity + options.Timeout > 0)
            {
                await StopAsync();
            }
        }
    }
}
