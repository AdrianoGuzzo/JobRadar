using JobRadar.Application.Profiles;
using JobRadar.Domain.Profiles;
using JobRadar.UnitTests.Domain;
using JobRadar.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobRadar.UnitTests.Application;

public sealed class SyncCandidateProfileTests
{
    private readonly InMemoryCandidateProfileRepository _profiles = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Create_Profile_When_None_Exists()
    {
        var result = await Sync().ExecuteAsync(CandidateProfileTests.SampleContent(), Ct);

        result.Should().Be(ProfileSyncResult.Created);
        _profiles.Profile!.Content.Should().BeEquivalentTo(CandidateProfileTests.SampleContent());
    }

    [Fact]
    public async Task Should_Keep_Identity_And_Change_Version_When_Content_Changes()
    {
        var original = CandidateProfile.Create(CandidateProfileTests.SampleContent());
        var originalVersion = original.Version;
        _profiles.Profile = original;

        var result = await Sync().ExecuteAsync(CandidateProfileTests.SampleContent() with { Seniority = "Especialista" }, Ct);

        result.Should().Be(ProfileSyncResult.Updated);
        _profiles.Profile.Id.Should().Be(original.Id, "as notificações já enviadas continuam valendo para o mesmo candidato");
        _profiles.Profile.Version.Should().NotBe(originalVersion);
        _profiles.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Should_Not_Save_When_Content_Is_The_Same()
    {
        _profiles.Profile = CandidateProfile.Create(CandidateProfileTests.SampleContent());

        var result = await Sync().ExecuteAsync(CandidateProfileTests.SampleContent(), Ct);

        result.Should().Be(ProfileSyncResult.Unchanged);
        _profiles.SaveCount.Should().Be(0);
    }

    private SyncCandidateProfile Sync() => new(_profiles, NullLogger<SyncCandidateProfile>.Instance);
}
