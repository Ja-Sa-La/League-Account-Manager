using System.Net;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Tokens;

[TestClass]
public class TokenSafetyTests
{
    [TestMethod]
    public void GetValidatedExtractionPath_AllowsChildPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "riot-config");

        var result = LoginTokenManager.GetValidatedExtractionPath(root, Path.Combine("nested", "file.yaml"));

        StringAssert.StartsWith(result, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        StringAssert.EndsWith(result, Path.Combine("nested", "file.yaml"), StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void GetValidatedExtractionPath_RejectsParentTraversalAndSiblingPrefixes()
    {
        var root = Path.Combine(Path.GetTempPath(), "riot-config");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            LoginTokenManager.GetValidatedExtractionPath(root, Path.Combine("..", "outside.yaml")));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            LoginTokenManager.GetValidatedExtractionPath(root, Path.Combine("..", "riot-config-other", "file")));
    }

    [TestMethod]
    public void IsSuccessfulResponse_RequiresTwoHundredStatusCode()
    {
        using var ok = new HttpResponseMessage(HttpStatusCode.OK);
        using var noContent = new HttpResponseMessage(HttpStatusCode.NoContent);
        using var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        using var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using var serverError = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        Assert.IsTrue(ProxyLoginTokenManager.IsSuccessfulResponse(ok));
        Assert.IsTrue(ProxyLoginTokenManager.IsSuccessfulResponse(noContent));
        Assert.IsFalse(ProxyLoginTokenManager.IsSuccessfulResponse(redirect));
        Assert.IsFalse(ProxyLoginTokenManager.IsSuccessfulResponse(unauthorized));
        Assert.IsFalse(ProxyLoginTokenManager.IsSuccessfulResponse(serverError));
        Assert.IsFalse(ProxyLoginTokenManager.IsSuccessfulResponse(null));
        Assert.IsFalse(ProxyLoginTokenManager.IsSuccessfulResponse(new object()));
    }
}