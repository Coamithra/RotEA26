# Backlog — open tickets

13 cards, in board order.

## 1. `bec47239` — in the host room menu, the cancel button is overlapped by "start when your crew is aboard!"

_No description or comments — the card title is the whole ticket._

## 2. `ed32efe1` — let's make the explosion that is created when you respawn have its size level be equal to the level you had the "2" powerup at

_No description or comments — the card title is the whole ticket._

## 3. `f6fc1d97` — in multiplayer games, on the client, I can see 1 hp ufo's blink white before they blow up (the hit effect for enemies with multiple hit points). Is this a result of how the networking works? That a joining peer just shows the "enemy hit" animation until the host acknowledges that the enemy died? For 1 hp enemies I'd like the joining clients to just immediately blow the monster up and send a message to the host. The host trusts clients and then also blows up that enemy (of if it already had it just ignores the message since it's already dealt with).

_No description or comments — the card title is the whole ticket._

## 4. `085ebddc` — we should probably reduce the max magnitude of screenshake by 50% (so a global reduction by 50% across the board)

_No description or comments — the card title is the whole ticket._

## 5. `c1cdd3e5` — On a joining client - while I was dead and respawning, the other players' ships (who respawned before me) did not appear on the playing field until mine did.

_No description or comments — the card title is the whole ticket._

## 6. `444eb614` — spider boss (lvl 2) - on a joining client, after the boss was defeated and its death animation played, the original sprite still appeared for a few frames

_No description or comments — the card title is the whole ticket._

## 7. `8732568e` — multiplayer games (on a joining peer side) seem to have a lot of loud explosion effect sounds. I suspect we get a big packet with a bunch of dead enemies and play the sound a couple of times in the same frame perhaps? I propose either adding a function to our audio engine to limit the nr of sounds played at exactly the same time (to one) or create special case code just for the explosion sfx.

_No description or comments — the card title is the whole ticket._

## 8. `d44a49a4` — the respawning timer gfx need a bit of tweaking.

**Description / notes:**

1) the middle circle is pure black, obstructing the game - should be transparent (can be slightly darkened but very subtle)

2) the growing ring is a bit too thick, make it about 60% of what it is now.

3) Needs to have the color of the player who will respawn there (rather than pink)

4) the text is not nicely vertically centered rn. Needs to move down a bit.

## 9. `745728f9` — space mines (lvl 3, aka death stars) seem to also explore when they reach a dead player's location

**Description / notes:**

also the homing sound doesnt play for joining clients

## 10. `c600c55a` — mission failed in multiplayer dumps everyone back to the menu :)

**Description / notes:**

should be the same as when you beat a level, the host can select a new level (or the same) to try.

## 11. `5f506d11` — the "parade of alien rulers" in level 3 - the alien ruler animation is quite jerky on a joining client. We can use the same client-side logic to smooth that out that we do for other ufos etc

**Description / notes:**

Also the alein ruler's death animation (shrink + explode) keeps playing repeatedly if I pause the game. And it's spawning far too many explosions (this is on joining client, not sure if that's a network thing, perhaps the client spawns them itself and then receives them as well from the host through networking?)

## 12. `430494a7` — just before the overmind final boss, when flying through the blocks (walls) that lead up to the boss, in the middle of the wall section there is a brief visual stutter where a different set of walls is shown, looks like a section from previously in the game. Lasts maybe 1-2 frames.

_No description or comments — the card title is the whole ticket._

## 13. `51566427` — in a multiplayer match, as long as the host doesnt disconnect we don't need to end the game

_No description or comments — the card title is the whole ticket._
