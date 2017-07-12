# OALMPlayer
Simple OpenAL Music Player

This is a very simple a rough OpenAL Music Player that I coded just to use the X-RAM on my Creative X-Fi.
It can decodes the sound file in one go and play it, or stream it, depending on compile flags for now.

This software uses CSCore, NVorbis, OpenTK and TagLib#.

TODO:
- 24-bit, 32-bit and float audio support
- Re-enable effects
- Use events instead of timers

KNOWN ISSUES:
- More than one sound may play when changing or selecting songs too fast (timer bug)
