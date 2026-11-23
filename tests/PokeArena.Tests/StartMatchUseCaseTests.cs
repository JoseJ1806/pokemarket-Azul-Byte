using PokeArena.Api.Modules.Matches.Application;
using PokeArena.Api.Modules.Matches.Domain;
using PokeArena.GameEngine;

namespace PokeArena.Tests;

public sealed class StartMatchUseCaseTests
{
    [Fact]
    public async Task StartMatchCreatesMatchWithInitialTurn()
    {
        var challenger = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var opponent = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var repository = new FakeMatchRepository(challenger, opponent);
        var useCase = new StartMatchUseCase(repository, new InitialMatchGameEngine());

        var result = await useCase.ExecuteAsync(
            new StartMatchCommand(
                "match-key-1",
                challenger,
                opponent,
                [1, 2],
                [3, 4],
                10),
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.False(result.IsReplay);
        Assert.NotNull(result.Match);
        Assert.Equal("Created", result.Match.Status);
        Assert.Equal(challenger, result.Match.CurrentTurnPlayerId);
        Assert.True(repository.Committed);
    }

    [Fact]
    public async Task StartMatchRejectsOpponentDeckCardNotOwned()
    {
        var challenger = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var opponent = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var repository = new FakeMatchRepository(challenger, opponent);
        repository.OpponentOwnedCards = [3];
        var useCase = new StartMatchUseCase(repository, new InitialMatchGameEngine());

        var result = await useCase.ExecuteAsync(
            new StartMatchCommand("match-key-2", challenger, opponent, [1, 2], [3, 4], 10),
            CancellationToken.None);

        Assert.Equal("deck-card-not-owned", result.Error?.Code);
        Assert.False(repository.Committed);
    }

    [Fact]
    public async Task RepeatedIdempotencyKeyReturnsReplay()
    {
        var challenger = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var opponent = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var repository = new FakeMatchRepository(challenger, opponent);
        var useCase = new StartMatchUseCase(repository, new InitialMatchGameEngine());
        var command = new StartMatchCommand("match-key-3", challenger, opponent, [1, 2], [3, 4], 10);

        var first = await useCase.ExecuteAsync(command, CancellationToken.None);
        var replay = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Match?.Id, replay.Match?.Id);
    }

    [Fact]
    public async Task EngineFailureReturnsUnavailableAndDoesNotCommit()
    {
        var challenger = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var opponent = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var repository = new FakeMatchRepository(challenger, opponent);
        var useCase = new StartMatchUseCase(repository, new FailingGameEngine());

        var result = await useCase.ExecuteAsync(
            new StartMatchCommand("match-key-4", challenger, opponent, [1, 2], [3, 4], 10),
            CancellationToken.None);

        Assert.Equal("game-engine-unavailable", result.Error?.Code);
        Assert.False(repository.Committed);
    }

    private sealed class FailingGameEngine : IGameEngine
    {
        public Task<MatchSnapshot> StartAsync(
            StartMatchRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Motor simulado.");
    }

    private sealed class FakeMatchRepository(
        Guid challenger,
        Guid opponent) : IMatchRepository
    {
        private readonly Dictionary<string, Match> matches = new();
        public IReadOnlyList<int> ChallengerOwnedCards { get; set; } = [1, 2];
        public IReadOnlyList<int> OpponentOwnedCards { get; set; } = [3, 4];
        public bool Committed { get; private set; }

        public Task<Match?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(matches.GetValueOrDefault(idempotencyKey));

        public Task<bool> PlayerExistsAsync(Guid playerId, CancellationToken cancellationToken) =>
            Task.FromResult(playerId == challenger || playerId == opponent);

        public Task<bool> LocationIsAvailableAsync(int locationId, CancellationToken cancellationToken) =>
            Task.FromResult(locationId == 10);

        public Task<IReadOnlyList<int>> GetOwnedCardIdsAsync(
            Guid playerId,
            IReadOnlyList<int> cardIds,
            CancellationToken cancellationToken)
        {
            var owned = playerId == challenger ? ChallengerOwnedCards : OpponentOwnedCards;
            return Task.FromResult<IReadOnlyList<int>>(cardIds.Where(owned.Contains).ToArray());
        }

        public Task<IMatchCreationScope> BeginCreationAsync(
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<IMatchCreationScope>(new FakeScope(this, idempotencyKey));

        private sealed class FakeScope(FakeMatchRepository owner, string idempotencyKey) : IMatchCreationScope
        {
            public Task<Match?> FindExistingAsync(CancellationToken cancellationToken) =>
                Task.FromResult(owner.matches.GetValueOrDefault(idempotencyKey));

            public Task CommitAsync(Match match, CancellationToken cancellationToken)
            {
                owner.matches[idempotencyKey] = match;
                owner.Committed = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
