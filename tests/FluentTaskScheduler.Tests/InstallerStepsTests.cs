namespace FluentTaskScheduler.Tests;

public class InstallerStepsTests
{
    [Theory]
    [InlineData(InstallerStep.Installed, "--veloapp-install", "2.0.0")]
    [InlineData(InstallerStep.Uninstalling, "--veloapp-uninstall", "2.0.0")]
    [InlineData(InstallerStep.Other, "--veloapp-updated", "2.0.1")]
    [InlineData(InstallerStep.Other, "--veloapp-obsolete", "2.0.0")]
    [InlineData(InstallerStep.Other, "--veloapp-version")]
    [InlineData(InstallerStep.Uninstalling, "--VeloApp-Uninstall", "2.0.0")]
    public void AStartByTheInstaller_IsToldByItsFirstArgument(InstallerStep expected, params string[] args) =>
        Assert.Equal(expected, InstallerSteps.Of(args));

    [Fact]
    public void AStepThisVersionHasNeverHeardOf_IsStillOneThatMustNotOpenTheWindow() =>
        Assert.Equal(InstallerStep.Other, InstallerSteps.Of(["--veloapp-something-new", "3.0.0"]));

    [Theory]
    [InlineData]
    [InlineData("--list")]
    [InlineData("--after-restart")]
    [InlineData("--run", "--veloapp-uninstall")]
    [InlineData("veloapp-uninstall")]
    public void AnyOtherStart_IsNotTheInstallers(params string[] args) =>
        Assert.Equal(InstallerStep.None, InstallerSteps.Of(args));
}
