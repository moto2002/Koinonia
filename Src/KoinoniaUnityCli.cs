using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.CSharp;
using UnityEngine;


namespace Koinonia
{
    public class KoinoniaUnityCli : ITerminalFrontend
    {

        private List<string> _linesContainer;
        private Dictionary<string, TerminalServerCommand> _cliCommands;
        private List<TerminalServerCommand> _helpCommands;
        private Dictionary<string, string> _cliAliases;

        public KoinoniaUnityCli()
        {
            var methods = GetType().GetMethods().Where(m => m.IsDefined(typeof (CLICommand), true));
            foreach (var methodInfo in methods)
            {
                var c = methodInfo.GetCustomAttributes(typeof (CLICommand), true).FirstOrDefault() as CLICommand;
                var info = methodInfo;

                Action<string[]> handler = str =>
                {
                    info.Invoke(this, new object[] { str });
                };

                CliCommands.Add(c.Code, new TerminalServerCommand()
                {
                    Action = handler,
                    CommandCode = c.Code,
                    Help = c.Help
                });

                var aliases = methodInfo.GetCustomAttributes(typeof(CLIAlias), true).Cast<CLIAlias>();

                foreach (var alias in aliases)
                {
                    CliAliases.Add(alias.Code,c.Code);
                }

            }

            Log("Hello, I am Koinonia!");
            Log("Type \"help\" or \"h\" for a list of available commands.");
            Log("");
            Log("");
         
        }

        public Dictionary<string, TerminalServerCommand> CliCommands
        {
            get { return _cliCommands ?? (_cliCommands = new Dictionary<string, TerminalServerCommand>()); }
            set { _cliCommands = value; }
        }

        public Dictionary<string, string> CliAliases
        {
            get { return _cliAliases ?? (_cliAliases = new Dictionary<string, string>()); }
            set { _cliAliases = value; }
        }

        public List<string> LinesContainer
        {
            get { return _linesContainer ?? (_linesContainer = new List<string>()); }
            set { _linesContainer = value; }
        }

        public event Action LinesUpdated;

        public IEnumerable<string> Lines
        {
            get
            {
                return LinesContainer;
            }
        }

        public Thread Post(string msg)
        {
            Log(">  "+msg);

            Thread thread = null;
            thread = new Thread(() =>
            {
                try
                {
                    Process(msg);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    throw;
                }
                finally
                {
                    Workers.Remove(thread);
                }
                
            });
            Workers.Add(thread);
            thread.Start();

            return thread;
        }

        public IEnumerable<TerminalServerCommand> Commands
        {
            get
            {
                return CliCommands.Values;
            }
        }

        public bool IsWorking
        {
            get { return Workers.Any(); }
        }

        public List<Thread> Workers
        {
            get { return _workers ?? (_workers = new List<Thread>()); }
            set { _workers = value; }
        }

        private void Process(string msg)
        {

            if (s != null)
            {
                response = msg;
                s.Set();
                return; //Intercepterd by running process 
                //TODO: introduce read queue later?
            }

            var args = msg.Split(new char[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
            var key = args[0];

            if (CliCommands.ContainsKey(key))
            {
                var handler = CliCommands[key];
                handler.Action(args);
            } else if (CliAliases.ContainsKey(key))
            {
                var handler = CliCommands[CliAliases[key]];
                handler.Action(args);
            }
            else
            {
                LogProblem("No Such Command");
            }

        }

        public void LogProblem(string error)
        {
            Log("Error: "+error);
        }

        public void LogWarning(string warning)
        {
            throw new NotImplementedException();
        }

        public void Log(string msg)
        {
            LinesContainer.Add(msg);
            if (LinesUpdated != null) LinesUpdated();
        }

        [CLICommand("github_token","Echo or set github token")]
        [CLIAlias("ghtoken")]
        public void GithubAccessToken(string[] args)
        {
            if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                KoinoniaApplication.AccessToken = args[1];
            Log("Github Access Token: "+KoinoniaApplication.AccessToken);
        }


        [CLICommand("finalize","Manually finish all the installs when needed")]
        public void Finalize(string[] args)
        {
            KoinoniaApplication.Instance.FinalizeInstalls();
        }

        [CLICommand("test_installer")]
        public void TestInstaller(string[] args)
        {
            KoinoniaApplication.Instance.TestInstaller(args[1],args[2]);
        }


        [CLICommand("help","Show list of available commands")]
        [CLIAlias("h")]
        public void Help(string[] args)
        {
            Log("Commands:");
            foreach (var cliCommand in CliCommands)
            {
                Log("   "+cliCommand.Value.CommandCode);
                if(!string.IsNullOrEmpty(cliCommand.Value.Help))
                Log("       "+cliCommand.Value.Help);

                var aliases = CliAliases.Where(v => v.Value == cliCommand.Value.CommandCode);
                if (aliases.Any())
                {
                    Log("       Aliases:");
                    foreach (var alias in aliases)
                    {
                        Log("           "+alias.Key);
                    }
                }
            }
        }

        [CLICommand("list","Show list of installed packages")]
        [CLIAlias("l")]
        public void List(string[] args)
        {

            KoinoniaApplication.Instance.FetchInstallsRegistry(true);
            foreach (var install in KoinoniaApplication.Instance.InstallsRegistry)  
            {
                Log(install.ToString());
            }

        }

        [CLICommand("uninstall","Uninstall package")]
        [CLIAlias("u")]
        public void Uninstall(string[] args)
        {
            throw new NotImplementedException("Uninstall is not yet implemented");
        }


        EventWaitHandle s;

        string response = null;

        private List<Thread> _workers;

        public string Read()
        {
            s = new ManualResetEvent(false);
            s.WaitOne();
            s.Close();
            s = null;
            return response;
        }

        [CLICommand("install","Install package")]
        [CLIAlias("i")]
        public void Install(string[] args)
        {

            GithubRepositoryEntry dec = GithubSchemeDecoder.Decode(args[1]);

            Log(string.Format("Will install {0} from {1} at {2}", dec.Name, dec.Owner, dec.Tag));

            var downloadableHost = new DownloadablesHost()
            {
                AuthorName = dec.Owner,
                RepositoryName = dec.Name
            };

            Log("Fetching downloadables...");

            downloadableHost.FetchDownloadables();

            Log(string.Format("{0}/{1} contains {2} downloadable entries...", dec.Owner, dec.Name, downloadableHost.Downloadables.Count));


            Downloadable d = downloadableHost.Downloadables.FirstOrDefault();

            if (!string.IsNullOrEmpty(dec.Tag))
            {
                d = downloadableHost.Downloadables.FirstOrDefault(_ => _.Name == dec.Tag);
            }

            if(d == null) throw new Exception("Downloadable Entry not found");

            Log(string.Format("Installing {0}/{1} @ {2} # {3}...", d.AuthorName, d.RepositoryName, d.Name,d.CommitSha)); 


            KoinoniaApplication.Instance.InstallNode(d);

        }


    }

}