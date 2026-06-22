Boss video feed for the "Quick Sync" Meridian Meet call (see ../office.js).

Files (all present):
  boss_poster.jpg     -- still fallback / <video> poster
  boss_idle.mp4       -- neutral "listening" loop (shown when the boss is idle)
  boss_talking.mp4    -- mouth-moving loop (overlaid while the boss delivers a line)

The call shows a 4:3 boss tile. The talking clip plays over the idle clip while a
caption types out; both are muted (dialogue is on-screen text, not VO). A <video>
is only revealed once it has loaded, so a missing/renamed clip falls back to the
poster with no black flash.

These were web-optimized from new_assets_raw/bossman_{idle,talking}.mp4 with the
ffmpeg bundled in Python's imageio-ffmpeg:
  ffmpeg -y -i bossman_<name>.mp4 -an -vf "scale=-2:'min(760,ih)'" \
    -c:v libx264 -profile:v high -pix_fmt yuv420p -crf 26 -preset slow \
    -movflags +faststart boss_<name>.mp4
To replace a clip, drop a new bossman_*.mp4 in new_assets_raw/ and re-run that.
Keep them short seamless loops; 4:3 framing matches the tile (other ratios are
object-fit: cover cropped around the face). They ship with `dotnet publish`.
