using agot_bg_website.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Soenneker.Validators.Email.Disposable.Online.Abstract;
using Xunit;

namespace agot_bg_website.Tests.Services;

/// <summary>
/// Covers DisposableEmailChecker's translation of the underlying validator's three-state result
/// into a plain bool, and its "fail open" behavior when the online domain-list source can't be
/// reached (see the class's own doc comment, and LOCAL_DEV_VERIFICATION.md's "Disposable email"
/// section) - registration/email-change must not be blocked outright just because the block-list
/// download failed.
/// </summary>
public class DisposableEmailCheckerTests
{
    private sealed class FakeValidator(bool? result, Exception? throwOnValidate = null)
        : IEmailDisposableOnlineValidator
    {
        public ValueTask WarmUp(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<bool?> Validate(
            string? email,
            CancellationToken cancellationToken = default
        )
        {
            if (throwOnValidate is not null)
            {
                throw throwOnValidate;
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() { }
    }

    [Fact]
    public async Task IsDisposableAsync_ValidatorReturnsFalse_MeansDomainIsListed_ReturnsTrue()
    {
        var sut = new DisposableEmailChecker(
            new FakeValidator(result: false),
            NullLogger<DisposableEmailChecker>.Instance
        );

        Assert.True(await sut.IsDisposableAsync("test@mailinator.com"));
    }

    [Fact]
    public async Task IsDisposableAsync_ValidatorReturnsTrue_MeansDomainIsNotListed_ReturnsFalse()
    {
        var sut = new DisposableEmailChecker(
            new FakeValidator(result: true),
            NullLogger<DisposableEmailChecker>.Instance
        );

        Assert.False(await sut.IsDisposableAsync("test@example.com"));
    }

    [Fact]
    public async Task IsDisposableAsync_ValidatorReturnsNull_EmptyListTreatedAsNotDisposable()
    {
        var sut = new DisposableEmailChecker(
            new FakeValidator(result: null),
            NullLogger<DisposableEmailChecker>.Instance
        );

        Assert.False(await sut.IsDisposableAsync("test@example.com"));
    }

    [Fact]
    public async Task IsDisposableAsync_ValidatorThrows_FailsOpenInsteadOfBlockingRegistration()
    {
        var sut = new DisposableEmailChecker(
            new FakeValidator(
                result: null,
                throwOnValidate: new HttpRequestException("list source unreachable")
            ),
            NullLogger<DisposableEmailChecker>.Instance
        );

        Assert.False(await sut.IsDisposableAsync("test@example.com"));
    }
}
