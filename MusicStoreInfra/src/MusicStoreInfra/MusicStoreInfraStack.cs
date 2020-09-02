using Amazon.CDK;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK.AWS.AutoScaling;
using Amazon.CDK.AWS.ECR;

namespace MusicStoreInfra
{
    public class MusicStoreInfraStack : Stack
    {
        private static string FormatConnectionString(string serverAddress, string dbName, object password) =>
            $"Server={serverAddress}; Database={dbName}; User Id=sa; Password={password}";

        internal MusicStoreInfraStack(Construct scope, string id = "Music-Store-Windows-Hosting-Env-Stack", IStackProps props = null) : base(scope, id, props)
        {
            var dbPasswordSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, "DbPasswordSecret", new SecretProps
            {
                SecretName = "music-store-database-password",
                GenerateSecretString = new SecretStringGenerator
                {
                    ExcludeCharacters = "/@\" ",
                    PasswordLength = 10,
                }
            });

            var vpc = new Vpc(this, "VPC", new VpcProps
            {
                MaxAzs = 3
            });

            var database = new DatabaseInstance(this, $"RDS-SQL-Server",
                new DatabaseInstanceProps
                {
                    Vpc = vpc,
                    InstanceType = InstanceType.Of(InstanceClass.BURSTABLE3, InstanceSize.SMALL),
                    VpcPlacement = new SubnetSelection
                    {
                        SubnetType = SubnetType.PRIVATE
                    },

                    DeletionProtection = false,
                    InstanceIdentifier = "Music-Store-SQL-Server",
                    Engine = DatabaseInstanceEngine.SqlServerWeb(new SqlServerWebInstanceEngineProps { Version = SqlServerEngineVersion.VER_14 }),
                    MasterUsername = "sa",
                    MasterUserPassword = dbPasswordSecret.SecretValue,
                    RemovalPolicy = RemovalPolicy.DESTROY
                }
            );

            string mainDbConnectionString = FormatConnectionString(database.DbInstanceEndpointAddress, "MusicStore", dbPasswordSecret.SecretValue);
            string identityDbConnectionString = FormatConnectionString(database.DbInstanceEndpointAddress, "Identity", dbPasswordSecret.SecretValue);

            var ecsCluster = new Cluster(this, "ECS-cluster", new ClusterProps
            {
                Vpc = vpc,
                ClusterName = "Music-Store-Windows"
            });

            var userData = UserData.ForWindows();
            userData.AddCommands(
                "Import-Module ECSTools",
                $"Initialize-ECSAgent -Cluster '{ecsCluster.ClusterName}' -EnableTaskIAMRole"
            );

            var autoScalingGroup = new AutoScalingGroup(this, "Ecs-Auto-Acaling-Group", new AutoScalingGroupProps
            {
                Vpc = vpc,
                InstanceType = InstanceType.Of(InstanceClass.MEMORY5_NVME_DRIVE, InstanceSize.LARGE),
                MachineImage = EcsOptimizedImage.Windows(WindowsOptimizedVersion.SERVER_2019),
                //VpcSubnets = new SubnetSelection { Subnets = vpc.PrivateSubnets.Concat(vpc.PublicSubnets).ToArray() },
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
                UserData = userData,
                AssociatePublicIpAddress = true,
            });

            var sg = new SecurityGroup(this, "ECS-EC2-SG", new SecurityGroupProps
            {
                Vpc = vpc
            });
            sg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80));

            autoScalingGroup.AddSecurityGroup(sg);

            ecsCluster.AddAutoScalingGroup(autoScalingGroup);

            var taskDef = new TaskDefinition(this, "TaskDef", new TaskDefinitionProps
            {
                NetworkMode = NetworkMode.NAT,
                
            });

            //string ecrUrl = Fn.ImportValue(BuildEnvStack.ecrRepoUrlOutputExportName);
            var ecrRepo = Repository.FromRepositoryName(this, "ExistingEcrRepository", "music-store-windows");

            var container = taskDef.AddContainer("ContainerDef", new ContainerDefinitionOptions
            {
                Essential = true,
                MemoryLimitMiB = 2048,
                Cpu = 1024,
                Image = ContainerImage.FromEcrRepository(ecrRepo, "latest"),
                Environment = new Dictionary<string, string>()
                {
                    { "MusicStoreEntities", mainDbConnectionString },
                    { "identitydb", identityDbConnectionString }
                }
            });

            container.AddPortMappings(new PortMapping
            {
                Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                ContainerPort = 80,
                HostPort = 8080
            });

            var ecsService = new Ec2Service(this, "ECS-Service", new Ec2ServiceProps
            {
                Cluster = ecsCluster,
                TaskDefinition = taskDef,
                //SecurityGroup
            });

            //var ecsService = new ApplicationLoadBalancedEc2Service(this, "ECS-Service", new ApplicationLoadBalancedEc2ServiceProps
            //{
            //    Cluster = ecsCluster,
            //    PublicLoadBalancer = true,
            //    MemoryLimitMiB = 2 * 1024,

            //    TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
            //    {
            //        Image = ContainerImage.FromRegistry(Fn.ImportValue(BuildEnvStack.ecrRepoUrlOutputExportName) + ":latest"),
            //        Environment = new Dictionary<string, string>()
            //        {
            //            { "MusicStoreEntities", mainDbConnectionString },
            //            { "identitydb", identityDbConnectionString }
            //        }
            //    },

            //    //TaskDefinition = new Ec2TaskDefinition(this, "TaskDef", new Ec2TaskDefinitionProps
            //    //{
            //    //    NetworkMode = NetworkMode.NAT
            //    //})
            //});

            //ecsService.TaskDefinition.AddContainer("ContainerDef", new ContainerDefinitionOptions
            //{
            //    Essential = true,
            //    Image = ContainerImage.FromRegistry(Fn.ImportValue(BuildEnvStack.ecrRepoUrlOutputExportName) + ":latest"),
            //    Environment = new Dictionary<string, string>()
            //    {
            //        { "MusicStoreEntities", mainDbConnectionString },
            //        { "identitydb", identityDbConnectionString }
            //    }
            //});

            database.Connections.AllowDefaultPortFrom(ecsCluster.Connections.SecurityGroups[0]);
        }
    }
}
