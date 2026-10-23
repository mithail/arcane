// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(StomachSystem))]
public sealed class StomachDigestionTest
{
    [Test]
    public async Task ReactionProductsAreTransferredToBloodstream()
    {
        (string[] Reagents, int[] Amounts, string Product)[] reactions =
        {
            (new[] { "Water", "TableSalt" }, new[] { 4, 1 }, "Saline"),
            (new[] { "WeldingFuel", "Hydrogen", "Carbon" }, new[] { 1, 1, 1 }, "Oil"),
            (new[] { "Nitrogen", "Phosphorus", "Potassium" }, new[] { 1, 1, 1 }, "EZNutrient"),
        };

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var bodySys = entMan.System<SharedBodySystem>();
        var solutionSys = entMan.System<SharedSolutionContainerSystem>();
        var stomachSys = entMan.System<StomachSystem>();

        var tickrate = cfg.GetCVar(CVars.NetTickrate);

        foreach (var reaction in reactions)
        {
            EntityUid body = default;
            EntityUid stomach = default;

            await server.WaitPost(() =>
            {
                mapSys.CreateMap(out var mapId);
                body = entMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(0.5f, 0.5f), mapId));
            });

            await server.WaitRunTicks(5);

            await server.WaitAssertion(() =>
            {
                Assert.That(bodySys.TryGetBodyOrganEntityComps<StomachComponent>(body, out var stomachs),
                    "Test body has no stomach");
                stomach = stomachs![0].Owner;
            });

            await server.WaitAssertion(() =>
            {
                var solution = new Solution();
                for (var i = 0; i < reaction.Reagents.Length; i++)
                    solution.AddReagent(reaction.Reagents[i], FixedPoint2.New(reaction.Amounts[i]));

                Assert.That(stomachSys.TryTransferSolution(stomach, solution),
                    $"Could not put {string.Join(" + ", reaction.Reagents)} into the stomach");
            });

            await server.WaitRunTicks(tickrate * 2);

            var mixture = string.Join(" + ", reaction.Reagents);

            await server.WaitAssertion(() =>
            {
                Assert.That(solutionSys.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out _, out var stomachSolution));
                Assert.That(stomachSolution!.ContainsPrototype(reaction.Product),
                    $"{mixture} did not react into {reaction.Product} inside the stomach");
            });

            var digested = false;
            var foundInBlood = false;
            for (var second = 0; second < 30; second++)
            {
                await server.WaitRunTicks(tickrate);
                await server.WaitAssertion(() =>
                {
                    Assert.That(solutionSys.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out _, out var stomachSolution));
                    if (stomachSolution!.ContainsPrototype(reaction.Product))
                        return;

                    digested = true;

                    Assert.That(solutionSys.TryGetSolution(body, BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood));
                    foundInBlood = blood!.ContainsPrototype(reaction.Product);
                });

                if (digested)
                    break;
            }

            Assert.That(digested, Is.True,
                $"{reaction.Product} stayed in the stomach instead of being digested");
            Assert.That(foundInBlood, Is.True,
                $"{reaction.Product} was not transferred into the bloodstream");
        }

        await pair.CleanReturnAsync();
    }
}
