import os
import random
import yt_dlp
import discord
from discord.ext import commands
from plexapi.myplex import MyPlexAccount
import asyncio
import datetime

current_song_title = ""
current_song_duration = 0


TOKEN = 'ENTER_BOT_TOKEN'
PLEX_USERNAME = 'ENTER_PLEX_USERNAME'
PLEX_PASSWORD = 'ENTER_PLEX_PASSWORD'

account = MyPlexAccount(PLEX_USERNAME, PLEX_PASSWORD)
plex = account.resource('ENTER_SERVER_NAME').connect()

intents = discord.Intents.all()
intents.messages = True

bot = commands.Bot(command_prefix='!', intents=intents)

queue = []

async def disconnect_after(ctx, duration):
    await asyncio.sleep(duration)
    voice_client = ctx.voice_client
    if voice_client and not voice_client.is_playing() and not voice_client.is_paused():
        try:
            await voice_client.disconnect()
        except AttributeError:
            pass

async def play_next_song(ctx):
    if len(queue) > 0:
        url, song_info, song_duration = queue.pop(0)
        await play_song(ctx, url, song_info, song_duration)

async def play_song(ctx, url, song_info, song_duration, queue_song=False):
    global current_song_title
    global current_song_duration

    # Connect to the voice channel if not already connected
    if ctx.voice_client is None:
        channel = ctx.author.voice.channel
        voice_client = await channel.connect()
    else:
        voice_client = ctx.voice_client

    # If the bot is already playing a song, add the new song to the queue
    if voice_client.is_playing():
        queue.append((url, song_info, song_duration))
        await ctx.send(f"🎵 Added to queue: {song_info}")
    else:
        # Play the song
        FFMPEG_OPTIONS = {
            'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5',
            'options': '-vn',
        }
        voice_client.play(discord.FFmpegPCMAudio(url, **FFMPEG_OPTIONS), after=lambda e: asyncio.run_coroutine_threadsafe(play_next_song(ctx), bot.loop))

        # Update the current song's title and duration
        current_song_title = song_info
        current_song_duration = song_duration

        if not queue_song:
            # Send a message about the song being played
            await ctx.send(f"🎵 Now playing: {song_info}")

        # Disconnect after the song has finished playing
        await disconnect_after(ctx, song_duration / 1000)  # Convert milliseconds to seconds


@bot.command(name='show_queue', help='Show the current queue')
async def show_queue(ctx):
    if not queue and (ctx.voice_client is None or not ctx.voice_client.is_playing()):
        await ctx.send("❌ There are no songs in the queue.")
    else:
        # Include the currently playing song
        if ctx.voice_client.is_playing():
            current_duration = str(datetime.timedelta(seconds=int(current_song_duration/1000)))
            queue_list = [f"🔊 Currently Playing: {current_song_title} ({current_duration})"]
        else:
            queue_list = []
        
        # Add the rest of the songs in the queue with their duration
        for idx, (track, title, duration) in enumerate(queue, start=1):
            duration = str(datetime.timedelta(seconds=int(duration/1000)))
            queue_list.append(f"{idx}. {title} ({duration})")

        queue_text = "\n".join(queue_list)
        await ctx.send(f"🎵 Current Queue:\n{queue_text}")

@bot.command(name='play', help='Play a song by title')
async def play(ctx, *, song_name):
    # Search for the song in the Plex library
    results = plex.search(song_name)
    tracks = [item for item in results if item.type == 'track']

    if tracks:
        if len(tracks) > 1:
            # Display the list of found tracks with numbers
            track_list = "\n".join([f"{idx + 1}. {track.grandparentTitle} - {track.title}" for idx, track in enumerate(tracks)])
            await ctx.send(f"🔍 Found multiple songs with the title '{song_name}':\n{track_list}\nPlease type the number of the song you want to play.")
            
            def check(msg):
                return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(tracks)
            
            try:
                # Wait for the user's response with the selected song's number
                response = await bot.wait_for("message", timeout=60.0, check=check)
                track = tracks[int(response.content) - 1]
            except asyncio.TimeoutError:
                return await ctx.send("❌ No response received. Please try again.")

        else:
            track = tracks[0]

        await ctx.send(f"🔍 Found a song with the title '{song_name}'.")

        is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()
        
        if not is_playing:
            await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration)
        else:
            queue.append((track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration))
            await ctx.send(f"🎵 Added to queue: {track.grandparentTitle} - {track.title}")

    else:
        await ctx.send(f"❌ Couldn't find a song with the title '{song_name}'.")

    # Play the next song in the queue if there is one
    await play_next_song(ctx)

@bot.command(name='skip', help='Skip the current song')
async def skip(ctx):
    voice_client = ctx.voice_client
    if voice_client and voice_client.is_playing():
        voice_client.stop()
        await ctx.send("⏭ Skipped the current song.")
        await play_next_song(ctx)
    else:
        await ctx.send("❌ There is no song currently playing.")

@bot.command(name='clear_queue', help='Clear the current queue')
async def clear_queue(ctx):
    global queue
    queue = []
    await ctx.send("🗑️ Cleared the current queue.")

@bot.command(name='remove_song', help='Remove a specific song from the queue')
async def remove_song(ctx, *, song_number: int):
    global queue
    if len(queue) >= song_number and song_number > 0:
        removed_song = queue.pop(song_number - 1)[1]
        await ctx.send(f"🗑️ Removed song '{removed_song}' from the queue.")
    else:
        await ctx.send("❌ Invalid song number.")

@bot.command(name='shuffle', help='Shuffle the current queue')
async def shuffle(ctx):
    global queue
    if len(queue) > 0:
        random.shuffle(queue)
        await ctx.send("🔀 Shuffled the current queue.")
    else:
        await ctx.send("❌ There are no songs in the queue to shuffle.")

@bot.event
async def on_command_error(ctx, error):
    if isinstance(error, commands.CommandNotFound):
        await ctx.send("❌ Invalid command. Use !help to see the available commands.")
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send("❌ Missing required argument. Use !help to see the command usage.")
    else:
        await ctx.send(f"❌ Error: {error}")

@bot.command(name='youtube', help='Play audio from a YouTube link')
async def youtube(ctx, *, video_url):
    # Check if the URL is a valid YouTube video URL
    if "youtube.com" not in video_url and "youtu.be" not in video_url:
        return await ctx.send("❌ Invalid YouTube link. Please provide a valid YouTube video URL.")
    
    # Extract audio information and download URL
    ydl_opts = {
        'format': 'bestaudio/best',
        'noplaylist': True,
        'outtmpl': 'downloads/%(title)s.%(ext)s',
        'quiet': True,
    }
    
    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(video_url, download=False)
            audio_url = info_dict['url']
            title = info_dict['title']
            duration = info_dict['duration']
        except Exception as e:
            return await ctx.send(f"❌ Error extracting audio from YouTube video: {e}")

    # Play the extracted audio in the voice channel
    await play_song(ctx, audio_url, f"📺 {title}", duration * 1000)  # Convert seconds to milliseconds

@bot.command(name='stop', help='Stop playing music and disconnect from the voice channel')
async def stop(ctx):
    global queue
    voice_client = ctx.voice_client
    if voice_client and (voice_client.is_playing() or voice_client.is_paused()):
        voice_client.stop()
        queue = []
        await ctx.send("⏹ Stopped playing music and cleared the queue.")
        await voice_client.disconnect()
    else:
        await ctx.send("❌ There is no song currently playing or paused.")

bot.run(TOKEN)