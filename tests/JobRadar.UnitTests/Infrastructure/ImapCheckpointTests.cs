using JobRadar.Infrastructure.LinkedIn;
using MailKit;

namespace JobRadar.UnitTests.Infrastructure;

public sealed class ImapCheckpointTests
{
    [Theory]
    [InlineData("7:42", 7u, 42u)]
    [InlineData("7:0", 7u, 0u)]
    public void Should_Read_Last_Uid_From_Checkpoint_Of_Same_Folder(string checkpoint, uint validity, uint expected)
    {
        ImapAlertMailbox.ParseCheckpoint(checkpoint, validity).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8:42")]
    [InlineData("lixo")]
    [InlineData("7:-1")]
    public void Should_Ignore_Checkpoint_From_Other_Folder_Or_Invalid(string? checkpoint)
    {
        ImapAlertMailbox.ParseCheckpoint(checkpoint, 7).Should().BeNull();
    }

    [Fact]
    public void Should_Drop_Already_Read_Uid_Returned_By_Star_Range()
    {
        // Pelo RFC 3501, "43:*" com o maior UID = 42 devolve o 42.
        IList<UniqueId> found = [new UniqueId(7, 42)];

        ImapAlertMailbox.SelectNew(found, lastUid: 42, max: 10).Should().BeEmpty();
    }

    [Fact]
    public void Should_Select_In_Uid_Order_Up_To_The_Limit()
    {
        IList<UniqueId> found = [new UniqueId(7, 50), new UniqueId(7, 44), new UniqueId(7, 47)];

        ImapAlertMailbox.SelectNew(found, lastUid: 43, max: 2).Select(uid => uid.Id).Should().Equal(44u, 47u);
    }
}
