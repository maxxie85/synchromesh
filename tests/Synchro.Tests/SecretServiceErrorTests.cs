using Synchro.Platform.Linux;
using Xunit;

namespace Synchro.Tests;

public class SecretServiceErrorTests
{
    [Theory]
    [InlineData("g-dbus-error-quark", 2, "The name is not activatable", true)] // dbus-broker, e.g. a Steam Deck
    [InlineData("g-dbus-error-quark", 2, "The name org.freedesktop.secrets was not provided by any .service files", true)]
    [InlineData("g-dbus-error-quark", 3, "The name org.freedesktop.secrets does not have an owner", true)]
    [InlineData("g-io-error-quark", 0, "Cannot autolaunch D-Bus without X11 $DISPLAY", true)]
    [InlineData("g-io-error-quark", 19, "Operation was cancelled", false)] // the user dismissed the unlock prompt
    [InlineData("secret-error", 1, "Cannot create an item in a locked collection", false)]
    public void Only_a_missing_keyring_lets_the_password_file_take_over(string domain, int code, string message, bool noKeyring)
        => Assert.Equal(noKeyring, new SecretInterop.GError(domain, code, message).MeansNoKeyring);
}
