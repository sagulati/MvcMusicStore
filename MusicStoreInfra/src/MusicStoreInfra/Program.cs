using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicStoreInfra
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            Stack[] stacks = 
            {
                new BuildEnvStack(app),
                new MusicStoreInfraStack(app)
            };

            Console.WriteLine($"Stack names that can be deployed: \"{string.Join("\", \"", stacks.Select(x => x.StackName))}\".");

            app.Synth();
        }
    }
}
