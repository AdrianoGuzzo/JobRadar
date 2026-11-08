using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.Profiles;
using JobRadar.UnitTests.Domain;

namespace JobRadar.UnitTests.Infrastructure;

public sealed class ProfileFileReaderTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("jobradar-profile-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Should_Read_The_Example_Profile_Shipped_With_The_Repository()
    {
        var content = ProfileFileReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "profile.example.json"));

        content.Should().BeEquivalentTo(CandidateProfileTests.SampleContent());
    }

    [Fact]
    public void Should_Default_Missing_Lists_To_Empty()
    {
        var content = ProfileFileReader.Read(Write("""{ "name": "Ana", "workModes": ["onsite"] }"""));

        content.Name.Should().Be("Ana");
        content.RequiredTechnologies.Should().BeEmpty();
        content.Locations.Should().BeEmpty();
        content.WorkModes.Should().Equal(WorkMode.OnSite);
    }

    [Fact]
    public void Should_Report_Missing_File_With_Its_Path()
    {
        var path = Path.Combine(_directory.FullName, "nao-existe.json");

        var act = () => ProfileFileReader.Read(path);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{path}*profile.example.json*");
    }

    [Fact]
    public void Should_Report_Invalid_Json_Clearly()
    {
        var act = () => ProfileFileReader.Read(Write("""{ "name": "Ana", "workModes": ["Teletrabalho"] }"""));

        act.Should().Throw<InvalidOperationException>().WithMessage("*perfil*inválido*");
    }

    [Fact]
    public void Should_Reject_Profile_Without_Name()
    {
        var act = () => ProfileFileReader.Read(Write("""{ "headline": "Dev" }"""));

        act.Should().Throw<InvalidOperationException>().WithMessage("*name*");
    }

    private string Write(string json)
    {
        var path = Path.Combine(_directory.FullName, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }
}
