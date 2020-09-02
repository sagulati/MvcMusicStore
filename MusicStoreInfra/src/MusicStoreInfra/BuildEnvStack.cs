using Amazon.CDK;
using Amazon.CDK.AWS.ECR;

namespace MusicStoreInfra
{
    public class BuildEnvStack : Stack
    {
        internal static string ecrRepoUrlOutputExportName = "Music-Store-Windows-ECR-Repo-Name";

        public BuildEnvStack(Construct scope, string id = "Music-Store-Windows-Build-Env-Stack", IStackProps props = null) : base(scope, id, props)
        {
            var ecrRepo = new Repository(this, "ECRrepo", new RepositoryProps
            {
                RepositoryName = "music-store-windows"
            });

            new CfnOutput(this, "Ecr-Repo-Name", new CfnOutputProps {
                ExportName = ecrRepoUrlOutputExportName,
                Value = ecrRepo.RepositoryName
            });
        }
    }
}
