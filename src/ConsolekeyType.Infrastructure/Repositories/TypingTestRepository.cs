using ConsolekeyType.Domain.Aggregates.TypingTestAggregate;
using Microsoft.Extensions.Options;

namespace ConsolekeyType.Infrastructure.Repositories;

public class TypingTestRepository : ITypingTestRepository
{
    private readonly string _connectionString;

    public TypingTestRepository(IOptions<DatabaseSettings> dbSettings)
        => _connectionString = dbSettings.Value.ConnectionString;

    public Result Save(TypingTest typingTest)
    {
        if (!typingTest.IsCompleted)
            return Result.Failure("Typing test is not completed");

        using var connection = new SQLiteConnection(_connectionString);
        using var command = new SQLiteCommand(connection);
        command.CommandText = @"
insert into typing_tests(
                        text, language_id, start_time, end_time, duration, cpm, wpm
)
values (@text, @language_id, @start_time, @end_time, @duration, @cpm, @wpm)
";
        command.Parameters.AddWithValue("@language_id", typingTest.Text.Language.Id);
        command.Parameters.AddWithValue("@text", typingTest.Text.ToString());
        command.Parameters.AddWithValue("@start_time", typingTest.StartTime);
        command.Parameters.AddWithValue("@end_time", typingTest.EndTime);
        command.Parameters.AddWithValue("@duration", typingTest.Duration.Value);
        command.Parameters.AddWithValue("@cpm", typingTest.CPM.Value);
        command.Parameters.AddWithValue("@wpm", typingTest.WPM.Value);

        connection.Open();
        var inserted = command.ExecuteNonQuery();

        return inserted == 1 ? Result.Success() : Result.Failure("Unable to connect to database");
    }

    public Result<IReadOnlyCollection<TypingTest>> GetAll()
    {
        using var connection = new SQLiteConnection(_connectionString);
        using var command = new SQLiteCommand(connection);
        command.CommandText = @"
select
    *
from
    typing_tests;
";

        var result = new List<TypingTest>();

        connection.Open();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var typingTest = Map(reader);
            if (typingTest.HasNoValue)
                return Result.Failure<IReadOnlyCollection<TypingTest>>(""); //TODO: specify error

            result.Add(typingTest.Value);
        }

        return result;
    }

    public Maybe<TypingTest> GetById(long id)
    {
        using var connection = new SQLiteConnection(_connectionString);
        using var command = new SQLiteCommand(connection);
        command.CommandText = @"
select
    *
from
    typing_tests
where id == @id;
";
        command.Parameters.AddWithValue("@id", id);

        connection.Open();

        using var reader = command.ExecuteReader();
        reader.Read();
        return Map(reader);
    }

    //TODO: use DTO instead of domain object?
    private Maybe<TypingTest> Map(IDataRecord reader)
    {
        var id = (long)reader["id"];
        var language = Language.FromId((int)reader["language_id"]);
        var textFromDb = Text.Create((string)reader["text"], language.Value).Value;
        var startTime = (DateTime)reader["start_time"];
        var endTime = (DateTime)reader["end_time"];
        var cpm = Convert.ToSingle(reader["cpm"]);
        var wpm = Convert.ToSingle(reader["wpm"]);

        var (_, isFailure, typingTest) = TypingTest.Create(textFromDb, id);

        if (isFailure)
            return Maybe.None;

        typingTest.Start(startTime);
        typingTest.End(endTime);

        typingTest.GetType().GetProperty("WPM")!.SetValue(typingTest, Maybe<float>.From(wpm));
        typingTest.GetType().GetProperty("CPM")!.SetValue(typingTest, Maybe<float>.From(cpm));

        return typingTest;
    }
}