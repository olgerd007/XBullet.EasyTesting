using System.Runtime.CompilerServices;
using System.Text;
using VerifyTests;

namespace TestApi.IntegrationTests;

public static class VerifyModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.UseEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
