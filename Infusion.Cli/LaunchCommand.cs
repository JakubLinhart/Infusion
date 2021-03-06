﻿using Infusion.Commands;
using Infusion.EngineScripts;
using Infusion.IO.Encryption.Login;
using Infusion.LegacyApi;
using Infusion.LegacyApi.Console;
using Infusion.Logging;
using Infusion.Proxy;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Infusion.Cli
{
    internal sealed class LaunchCommand
    {
        private readonly IConsole console  = new TextConsole();
        private readonly ILogger diagnostic;
        private readonly CommandHandler commandHandler;
        private readonly LaunchOptions options;
        private ScriptEngine scriptEngine;
        private CSharpScriptEngine csharpScriptEngine;
        private string scriptFileName;

        public LaunchCommand(LaunchOptions options)
        {
            diagnostic = console;
            commandHandler = new CommandHandler(diagnostic);
            this.options = options;
            this.scriptFileName = options.ScriptFileName;
        }

        public void Execute()
        {
            var proxy = new InfusionProxy();

            if (options.CommandPrefix.Length > 0)
                commandHandler.CommandPrefix = options.CommandPrefix;
            else
                console.Error("Command prefix must contain at least one character, using default prefix.");

            proxy.Initialize(commandHandler, new NullSoundPlayer(), new LegacyApi.Injection.NullInjectionWindow());

            var resolvedServerAddress = Dns.GetHostEntry(options.ServerAddress)
                .AddressList.First(a => a.AddressFamily == AddressFamily.InterNetwork);

            proxy.Start(new ProxyStartConfig()
            {
                ServerAddress = options.ServerAddress,
                ServerEndPoint = new IPEndPoint(resolvedServerAddress, options.Port),
                LocalProxyPort = (ushort)options.LocalPort,
                ProtocolVersion = options.ProtocolVersion,
                LoginEncryptionKey = LoginEncryptionKey.FromVersion(options.ProtocolVersion),
                Encryption = GetEncryptionSetup(options.Encryption),
            });

            UO.CommandHandler.RegisterCommand(new Command("reload", Reload, false, true,
                "Reloads an initial script file."));
            UO.CommandHandler.RegisterCommand(new Command("load", LoadCommand, false, true,
                "Load a script file."));

            csharpScriptEngine = new CSharpScriptEngine(console);
            scriptEngine = new ScriptEngine(csharpScriptEngine, new InjectionScriptEngine(UO.Injection, console));

            console.Info($"\nConfigure your client to use 'localhost,{options.LocalPort}' as Ultima Online server address.");

            if (!string.IsNullOrEmpty(options.ScriptFileName))
            {
                Load(options.ScriptFileName);
            }

            Console.ReadLine();
        }

        private EncryptionSetup GetEncryptionSetup(ClientEncryption encryption)
        {
            switch (encryption)
            {
                case ClientEncryption.add:
                    return EncryptionSetup.EncryptedServer;
                case ClientEncryption.auto:
                    return EncryptionSetup.Autodetect;
                case ClientEncryption.remove:
                    return EncryptionSetup.EncryptedClient;
                default:
                    throw new NotImplementedException($"Unknown ClientEncryption value '{encryption}'");
            }
        }

        private void LoadCommand(string scriptFileName)
        {
            if (string.IsNullOrEmpty(scriptFileName))
            {
                console.Error("Missing script file name.");
                return;
            }

            string name = Path.GetFileName(scriptFileName);
            var scriptPath = Path.GetDirectoryName(scriptFileName);

            scriptFileName = Path.Combine(scriptPath, name);

            this.scriptFileName = scriptFileName;
            scriptEngine.ScriptRootPath = scriptPath;

            Reload();
        }

        private async void Load(string scriptFileName)
        {
            UO.CommandHandler.BeginTerminate(true);
            UO.CommandHandler.UnregisterAllPublic();

            scriptEngine.Reset();
            using (var tokenSource = new CancellationTokenSource())
            {
                await scriptEngine.ExecuteScript(scriptFileName, tokenSource);
            }
        }

        private void Reload()
        {
            Load(scriptFileName);
        }
    }
}
