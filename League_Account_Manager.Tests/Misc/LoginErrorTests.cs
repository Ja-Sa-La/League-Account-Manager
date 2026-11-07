using System.IO;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class LoginErrorTests
{
    [TestMethod]
    public void Describe_AuthFailureMarksCredentialsInvalid()
    {
        var error = RsoLoginService.LoginError.Describe("auth_failure");

        Assert.AreEqual("auth_failure", error.Code);
        Assert.IsTrue(error.InvalidCredentials);
        StringAssert.Contains(error.Message, "username or password");
    }

    [TestMethod]
    public void Describe_AccountStateIsNotTreatedAsBadCredentials()
    {
        foreach (var code in new[] { "account_disabled", "archived_account", "rate_limited", "pending_forget" })
        {
            var error = RsoLoginService.LoginError.Describe(code);

            Assert.AreEqual(code, error.Code);
            Assert.IsFalse(error.InvalidCredentials, code);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message), code);
        }
    }

    [TestMethod]
    public void Describe_UsesTheClientsFirstMatchOrder()
    {
        // The client checks invalid_request before auth_failure, so a combined reason follows that.
        var error = RsoLoginService.LoginError.Describe("invalid_request: auth_failure");

        Assert.AreEqual("invalid_request", error.Code);
        Assert.IsFalse(error.InvalidCredentials);
    }

    [TestMethod]
    public void Describe_CaptchaRejectionMarksTheBrowserProfileBurned()
    {
        foreach (var code in new[] { "captcha_not_allowed", "invalid_captcha", "captcha token rejected" })
        {
            var error = RsoLoginService.LoginError.Describe(code);

            Assert.IsTrue(error.BurnedCaptchaProfile, code);
            Assert.IsFalse(error.InvalidCredentials, code);
        }
    }

    [TestMethod]
    public void Describe_CredentialFailureDoesNotResetTheChallengeProfile()
    {
        var error = RsoLoginService.LoginError.Describe("auth_failure");

        Assert.IsFalse(error.BurnedCaptchaProfile);
    }

    [TestMethod]
    public void TryDeleteUserData_RemovesTheChallengeProfile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lam-webview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "EBWebView"));
        File.WriteAllText(Path.Combine(folder, "EBWebView", "Cookies"), "burned");
        ChallengeWindow.UserDataFolderOverride = folder;
        try
        {
            Assert.IsTrue(ChallengeWindow.TryDeleteUserData());
            Assert.IsFalse(Directory.Exists(folder));

            Directory.CreateDirectory(folder);
            Assert.IsFalse(ChallengeWindow.NoteLoginFailed());
            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsTrue(ChallengeWindow.NoteLoginFailed());
            Assert.IsFalse(Directory.Exists(folder));
            ChallengeWindow.NoteLoginSucceeded();
        }
        finally
        {
            ChallengeWindow.UserDataFolderOverride = null;
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Describe_KeepsAnUnknownCodeVisible()
    {
        var error = RsoLoginService.LoginError.Describe("something_new");

        Assert.AreEqual("something_new", error.Code);
        StringAssert.Contains(error.Message, "something_new");
        Assert.IsFalse(error.InvalidCredentials);
    }
}
