# A boar's trust

This quest is supplied by the optional `RobleysTestQuests` plugin. Install it alongside `RobleysQuestingFramework` on the server and clients.

Approaching within 12 metres of a generated `Runestone_Boars` location spawns one friendly, untamed two-star boar beside the visitor. Each stone spawns its quest boar only once per saved world. This uses a world global key, so revisiting, reconnecting, and normal world reloads do not duplicate the encounter. Other runestone types do not trigger it.

The quest becomes discovered at this encounter and awaits interaction to start. Every player continues watching for new stones, including while another boar quest is active or finished. Another player may accept an existing unclaimed boar first, in which case the original discoverer's quest is cancelled when its giver is claimed.

Interact with the boar to accept **A boar's trust**. The first player to accept claims that boar's quest. Bring **100 raspberries in that player's inventory**, then interact again to hand them in. Multiple inventory stacks count. There are no partial or shared contributions. The payment removes exactly 100 raspberries and automatically tames the same two-star boar. Normal ground feeding cannot bypass the quest. After completion it uses ordinary tamed-boar behavior.

Each stone offers an independent quest, with one quest boar per stone per world. A character can have multiple boar quests active at once; completion or cancellation at one stone does not block another. Existing saved quests keep their original progress and boar association. An active quest must be handed in at its original boar. If that boar is destroyed before completion, the quest is cancelled; the stone does not spawn a replacement. The quest boar is peaceful but not invulnerable.

If any player hits the boar before completion (including before accepting), it immediately becomes an ordinary hostile boar and targets the attacker. Its active quest is cancelled, pending hand-in authorization is revoked, and it no longer offers this quest. The betrayal flag persists in the boar's world data across reloads. Environmental damage and attacks from non-player creatures do not trigger betrayal. Once the quest is completed, the tamed boar uses normal game behavior.

## Multiplayer and saves

Install the mod on the server and every client. The server validates the connected character, boar identity, proximity, quest ownership, and completion state. As with the framework's existing inventory observations, the mod trusts the connected player's client to inspect and mutate its own inventory. This is not a server-authoritative inventory/anti-cheat system.

The client checks its live inventory and removes the berries before acknowledging payment. A receipt in the character's custom data prevents charging again when a payment request is repeated. Reconnect and interact again if a receipt was interrupted. Quest giver IDs are now included in quest saves; older saves default to no giver. Completed quests reapply the boar's reward marker if necessary, and the boar's current network owner applies taming after loading or changing ownership.

Character inventory, quest JSON, and the world use separate saves. Normal save/reload is supported; hard crashes or rolling back only one of these saves are not an atomic transaction and can leave them inconsistent.

## Verification

Automated regression checks run with `dotnet run --project tests/Persistence/Persistence.csproj`. They cover discovery without auto-start, the 99/100 threshold, loss of readiness when berries are removed, no automatic completion before hand-in, exact giver identity after save/reload, and a single completion reward. These tests stub the game services; they do not exercise real networking, spawning, or Unity behavior.

The quest is self-contained in `BoarRunestoneQuest.cs`; see [Writing quests](QUEST-AUTHORING.md) for the reusable APIs. Additional event-driven checks cover first-claim behavior, attacks before/after acceptance, missing givers, late payment callbacks after cancellation, listener cleanup, and generic multi-stack/quality-aware removal plans.

In game, verify on both a local world and a dedicated server:

1. Approach a boar runestone. Confirm exactly one new two-star boar appears and remains untamed and peaceful. Revisit and reconnect; no extra quest boar should appear.
2. Interact to accept. Confirm the journal shows the quest and berry count. A second player cannot claim that boar or pay for its owner.
3. Try 99 raspberries: nothing is removed or tamed. Split 100 berries across stacks, interact, and confirm exactly 100 are removed and the same boar is tamed with two stars. Extra berries remain.
4. Repeated interactions must not remove another payment. Ground feeding before completion must not tame it or consume dropped food.
5. Save/restart before hand-in, after acceptance, and after completion. Confirm the same boar, quest owner, stars, and taming state persist.
6. Disconnect around hand-in and reconnect. Retry interaction if unfinished and confirm a saved receipt does not charge twice. Test ownership transfer by having the accepting player leave while another player stays nearby.
7. Destroy an unfinished quest boar and verify cancellation. Ordinary boars and other runestones must retain their normal behavior.
8. Hit a quest boar before acceptance and during an active quest, using both melee and ranged attacks. Confirm immediate hostility, cancellation in the journal, no further quest interaction or reward, and no replacement spawn. Repeat with a second player attacking and with the boar owned by another peer. Save/reload and verify hostility and cancellation persist. Confirm environmental damage does not cancel the quest.

9. Visit two different boar stones with the same character and accept both quests. Pay one boar and verify the other remains unfinished. Complete or cancel each, then visit a third stone. Save/restart and confirm all outcomes persist and old stones never respawn their boars.
