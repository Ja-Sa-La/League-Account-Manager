using System.Net;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Tokens;

[TestClass]
public class TokenSafetyTests
{
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