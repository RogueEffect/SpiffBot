﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spiff.Core.API;
using Spiff.Core.API.Commands;
using Spiff.Core.API.EventArgs;
using Spiff.Core.IRC;
using Spiff.Core.Utils;

namespace Spiff.Core
{
    public class TwitchIRC
    {
        //Public Vars
        public string Channel { get; private set; }
        public string BotName { get; private set; }
        private readonly string _oauth;

        //Client vars
        public OutUtils WriteOut { get; set; }

        //event Args
        public event EventHandler<OnUserJoinEvent> OnUserJoinHandler;
        public event EventHandler<OnUserLeftEvent> OnUserLeftHandler;
        public event EventHandler<OnChatEvent> OnChatHandler;
        public event EventHandler<OnCommandEvent> OnCommandHandler;

        //Command List
        public Dictionary<string, Command> Commands {get; private set; }
        public List<Plugin> BotPlugins {get; private set;}
        private readonly Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();  

        //Instance
        public static TwitchIRC Instance { get; private set; }

        //Server IRC stuff
        public IRCClient IrcClient;

        public TwitchIRC(string channel, string botName, string outh)
        {
            Channel = channel;
            BotName = botName;
            _oauth = outh;
            Commands = new Dictionary<string, Command>();
            BotPlugins = new List<Plugin>();

            Instance = this;

            IrcClient = new IRCClient(channel, botName, outh, this);

            IrcClient.OnTwitchEvent += IrcClientOnOnTwitchEvent;

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
        }

        private Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            //Nasty hack to fix talking between plugins but works the best maybe will find a better way later
            return _loadedAssemblies[args.Name];
        }

        #region Publics
        public void AddCommand(Command command)
        {
            Command outcommand;
            Commands.TryGetValue("!" + command.CommandName, out outcommand);

            if (outcommand == null)
            {
                Commands.Add("!" + command.CommandName, command);
            }
        }

        public void RemoveCommand(Command command)
        {
            Command outcommand;
            Commands.TryGetValue("!" + command.CommandName, out outcommand);

            if (outcommand == null)
            {
                Commands.Remove("!" + command.CommandName);
            }
        }

        public void RemoveCommand(string command)
        {
            Command outcommand;
            Commands.TryGetValue("!" + command, out outcommand);

            if (outcommand == null)
            {
                Commands.Remove("!" + command);
            }
        }

        public Dictionary<string, Command> AllCommands()
        {
            return Commands;
        }

        public List<Plugin> AllPlugins()
        {
            return BotPlugins;
        } 

        public void LoadPlugin(Assembly plugin, bool start = false)
        {
            if (plugin == null) return;
            var types = plugin.GetTypes();
            foreach (var pin in from type in types where !type.IsInterface && !type.IsAbstract where type.IsSubclassOf(typeof(Plugin)) select (Plugin) Activator.CreateInstance(type))
            {
                Logger.Info(string.Format("Loading Plugin - {0}(V -> {1})", pin.Name, pin.Version), "Plugin Engine");
                if(start)
                    pin.Start();
                BotPlugins.Add(pin);
                break;
            }
        }

        public void RegisterAssembly(Assembly plugin)
        {
            if (!_loadedAssemblies.ContainsKey(plugin.FullName))
                _loadedAssemblies.Add(plugin.FullName, plugin);
        }


        public void StartPlugins()
        {
            foreach (var plugin in BotPlugins)
            {
                plugin.Start();
            }
        }

        #endregion

        #region Privates
        private void IrcClientOnOnTwitchEvent(object sender, TwitchEvent twitchEvent)
        {
            string data = twitchEvent.Payload;
            string message = "";

            string[] split1 = data.Split(':');
            if (split1.Length > 1)
            {
                //Splitting nick, type, chan and message
                var split2 = split1[1].Split(' ');

                //Nick consists of various things - we only want the nick itself
                var nick = split2[0];
                nick = nick.Split('!')[0];

                //Type = PRIVMSG for normal messages. Only thing we need
                var type = split2[1];

                //Channel posted to
                var channel = split2[2];

                if (split1.Length > 2)
                {
                    for (var i = 2; i < split1.Length; i++)
                    {
                        message += split1[i] + " ";
                    }
                }

                switch (type)
                {
                    case "PRIVMSG":
                        if (channel.StartsWith("#"))
                            if (OnChatHandler != null)
                                OnChatHandler(this, new OnChatEvent(channel, nick, message));
                        break;
                    case "JOIN":
                        if (channel.StartsWith("#"))
                            if (OnUserJoinHandler != null)
                                OnUserJoinHandler(this, new OnUserJoinEvent(nick, channel));
                        break;
                    case "PART":
                        if (channel.StartsWith("#"))
                            if (OnUserLeftHandler != null)
                                OnUserLeftHandler(this, new OnUserLeftEvent(nick, channel));
                        break;
                }

                if (message.StartsWith("!"))
                {
                    Command command;
                    Commands.TryGetValue(message.Split(' ')[0], out command);

                    if (command != null)
                    {
                        var args = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (OnCommandHandler != null)
                            OnCommandHandler(this, new OnCommandEvent(command, args, message));

                        command.Run(args, message, channel.TrimStart('#'), nick);
                    }
                }
            }
        }
        #endregion
    }
}
