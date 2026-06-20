# Research: Affordable online TTS for an arcade-style announcer ("POWER UP!", "DANGER!")

_Researched 2026-06-20. Grounded-research method: every claim traces to a gathered source._

## Questions
- **Q1:** Which cloud TTS can actually nail the hype arcade-announcer delivery?
- **Q2:** Which is affordable, and what does licensing look like for shipping the audio in a released game?
- **Q3:** What controls get you that shouted/excited energy?

## Bottom line first
For the actual use case — a finite set of announcer barks generated **once** and shipped as audio
files — the recurring monthly price barely matters. One month of a cheap plan (or even free credits)
covers an entire game's worth of lines. So the real deciders are **delivery quality** and **license
to ship**. On both, **ElevenLabs v3 (Starter, $5/mo)** is the strongest fit, with **OpenAI
gpt-4o-mini-tts** as the cheapest programmatic alternative.

## Findings

### ElevenLabs v3 has literal `[shouts]` / `[excited]` tags — built for exactly this
The one engine where the arcade-announcer direction is a first-class feature.
- "The article lists `[shouts]` and `[SHOUTING]` as delivery-direction tags... 'volume and energy
  for scenes that need restraint or force.'" [S4]
- Emotional tags include "[excited], [nervous], [frustrated]... combined or sequenced for richer
  emotional arcs." [S3]
- Tags can be combined, e.g. `[excited][shouts] This is amazing!` [S1]

Workflow: `[excited][shouts] POWER UP!` or `[shouts] DANGER!`. Caveat: v3 interprets tags
*contextually* — "small changes can produce different results," so iterate on placement. [S1]

### Pricing & licensing — the part that matters for a shipped game
- **ElevenLabs free tier is NOT usable in a released game:** "The free plan does not include
  commercial usage rights — you must attribute ElevenLabs." ~10,000 credits/mo (~10 min). [S8]
- **Commercial use starts at $5/mo:** "you need at minimum the Starter plan at $5/month." "All
  ElevenLabs paid plans include full commercial usage rights." [S8][S2]
- Cheapest entry of the majors: "ElevenLabs Starter at $5/mo... Murf AI starting at $23/mo and
  Play.ht at $31.20/mo." [S2]

Practical move: subscribe to Starter for one month, generate every bark, export, cancel. ~$5 total.

### OpenAI gpt-4o-mini-tts — cheapest if you want an API + steer-by-prompt
- "you can _tell_ it how to speak... Pass an `instructions` parameter like 'Speak in a warm,
  reassuring tone...' and the model adjusts delivery accordingly." [S5]
- Cost: "roughly $15/1M characters" / "~$0.015 per minute"; style prompt cost "negligible." [S5][S6]
- 13 voices, steerable tone/emotion via prompts. [S6]

Trade-off: steer with a prose instruction rather than inline `[shouts]` tags; API/code path rather
than click-and-export web UI. Likely a touch less theatrical for pure shout-energy, but far cheaper
at volume and trivially scriptable.

### Don't bother with PlayHT
- "PlayHT was acquired by Meta in July 2025 and permanently shut down on December 31, 2025." [S2]

### Free / novelty route: FakeYou — fun, but licensing is murky for a shipped game
- "FakeYou is a free, community-driven text-to-speech platform that specializes in pop culture
  voices, fan-made models, and novelty audio... from cartoons, video games, movies." [S7]
- But: "FakeYou is designed more for fun than commercial use." [S7]

Great for prototyping the *vibe* (ready-made announcer/game voices, zero prompt-engineering); real
licensing risk for a publicly released game. Uberduck: free tier 300 credits, paid from $4/mo. [S7]

### Honorable mentions for "expressive/gaming" (price less verified)
- **Typecast** — "expressive voice styles, intuitive emotion controls." [S1]
- **ReadSpeaker** — gaming-focused, "integrated in arcade games like Konami's 'QuizKnock
  STADIUM'." [S1]
- **Inworld AI** — "specifically supports in-game announcements." [S1]
- **Murf** — Creator ~$19-23/mo with commercial rights. [S1][S2]

## Recommendation
1. **Best delivery → ElevenLabs v3, Starter $5/mo.** `[shouts]`/`[excited]` tags purpose-built;
   generate all barks in one billing month and cancel. [S2][S4][S8]
2. **Cheapest scriptable pipeline → OpenAI gpt-4o-mini-tts** (~$0.015/min, steer via
   `instructions`). [S5][S6]
3. **Free prototyping of the vibe → FakeYou**, but re-license before shipping. [S7]

## Gaps and Uncertainties
- "$5 = full commercial rights" comes from secondary blogs [S2][S8], not a verbatim ToS quote —
  confirm on ElevenLabs' own terms before shipping.
- No source demonstrates a heard arcade-announcer sample; fit inferred from documented
  tags/steerability.
- Typecast / ReadSpeaker / Inworld / Murf pricing is from search snippets [S1][S2], not their pages.
- FakeYou licensing is per-voice and unconfirmed [S7].
- Offline/local TTS and voice-actor hire were out of scope.

## Sources
- [S1] WebSearch — "best AI text to speech for game announcer voice" -> Typecast, ElevenLabs, ReadSpeaker, Inworld
- [S2] WebSearch — "ElevenLabs vs PlayHT vs Murf pricing 2026" -> pricing + PlayHT shutdown
- [S3] https://elevenlabs.io/blog/eleven-v3-audio-tags-expressing-emotional-context-in-speech — emotional tag list
- [S4] https://elevenlabs.io/blog/v3-audiotags — `[shouts]`/`[SHOUTING]` delivery tags
- [S5] https://texttolab.com/blog/openai-tts-pricing — OpenAI TTS pricing + `instructions` steerability
- [S6] WebSearch — "gpt-4o-mini-tts pricing 2026" -> $0.015/min, 13 voices, steerable
- [S7] WebSearch — "FakeYou Uberduck arcade announcer TTS free 2026" -> novelty/game voices, commercial caveats
- [S8] WebSearch — "ElevenLabs free plan commercial use license 2026" -> free-tier limits, $5 commercial threshold
