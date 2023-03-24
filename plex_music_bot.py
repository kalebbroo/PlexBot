import os
import random
import yt_dlp
import discord
from discord.ext import commands
from plexapi.myplex import MyPlexAccount
import asyncio
import datetime
import math
import sys

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
        embed = discord.Embed(title=f"🎵 Added to queue: {song_info, song_duration / 1000}", color=0x00b0f0)
        await ctx.send(embed=embed)
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
        duration_seconds = int(song_duration / 1000)
        formatted_duration = str(datetime.timedelta(seconds=duration_seconds))

        if not queue_song:
            # Send an embed with the now playing information
            embed = discord.Embed(title=f"🎵 Now playing: {song_info} ({formatted_duration})", color=0x00b0f0)
            await ctx.send(embed=embed)

        # Disconnect after the song has finished playing and wait 2 minutes
        await disconnect_after(ctx, duration_seconds + 120)  # Add 120 seconds (2 minutes) to the duration

@bot.command(name='play', help='Play a song by title and artist')
async def play(ctx, *, query):
    # Search for the song in the Plex library
    results = plex.search(query)
    tracks = [item for item in results if item.type == 'track']

    if tracks:
        matching_tracks = []

        # Find tracks with both the song title and artist in the query
        for track in tracks:
            if all(word.lower() in (track.title + " " + track.grandparentTitle).lower() for word in query.split()):
                matching_tracks.append(track)

        if not matching_tracks:
            matching_tracks = tracks[:10]

        if len(matching_tracks) > 1:
            # Display the list of found tracks with numbers
            track_list = [f"{idx + 1}. {track.grandparentTitle} - {track.title}" for idx, track in enumerate(matching_tracks)]
            embed = discord.Embed(title="🔍 Found multiple songs for your query", description="\n".join(track_list), color=0x00b0f0)
            query_msg = await ctx.send(embed=embed)
            
            def check(msg):
                return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(track_list)
            
            try:
                # Wait for the user's response with the selected song's number
                response = await bot.wait_for("message", timeout=60.0, check=check)
                track = matching_tracks[int(response.content) - 1]
            except asyncio.TimeoutError:
                return await ctx.send("❌ No response received. Please try again.")

        else:
            track = matching_tracks[0]

        await ctx.send(f"🔍 Found a song for '{query}'.")

        is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()
        
        if not is_playing:
            await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration)
        else:
            queue.append((track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration))
            await ctx.send(f"🎵 Added to queue: {track.grandparentTitle} - {track.title}")

    else:
        await ctx.send(f"❌ Couldn't find a song for '{query}'.")

    # Play the next song in the queue if there is one
    await play_next_song(ctx)

async def send_queue(queue_list, page):
    start = (page - 1) * 10
    end = start + 10
    embed = discord.Embed(title="🎵 Current Queue", description="\n".join(queue_list[start:end]), color=0x00b0f0)
    embed.set_footer(text=f"Page {page}/{(len(queue_list) - 1) // 10 + 1}")
    return embed

def reaction_check(reaction, user):
    return user != bot.user and str(reaction.emoji) in ["⬅️", "➡️"]

@bot.command(name='show_queue', help='Show the current queue')
async def show_queue(ctx, page: int = 1):
    if not queue and (ctx.voice_client is None or not ctx.voice_client.is_playing()):
        await ctx.send("❌ There are no songs in the queue.")
    else:
        # Include the currently playing song
        if ctx.voice_client.is_playing() and current_song_duration is not None:
            current_duration = str(datetime.timedelta(seconds=int(current_song_duration/1000)))
            queue_list = [f"🔊 Currently Playing: {current_song_title} ({current_duration})"]
        else:
            queue_list = []

        # Add the rest of the songs in the queue with their duration
        for idx, (track, title, duration) in enumerate(queue, start=1):
            if duration is not None:
                duration = str(datetime.timedelta(seconds=int(duration/1000)))
            else:
                duration = "Unknown"
            queue_list.append(f"{idx}. {title} ({duration})")

        num_pages = (len(queue_list) - 1) // 10 + 1

    if 1 <= page <= num_pages:
        queue_msg = await ctx.send(embed=await send_queue(queue_list, page))
        await queue_msg.add_reaction("⬅️")
        await queue_msg.add_reaction("➡️")

        while True:
            try:
                reaction, user = await bot.wait_for("reaction_add", check=reaction_check, timeout=60)
            except asyncio.TimeoutError:
                break

            if str(reaction.emoji) == "⬅️" and page > 1:
                page -= 1
            elif str(reaction.emoji) == "➡️" and page < num_pages:
                page += 1
            else:
                await queue_msg.remove_reaction(reaction, user)
                continue

            await queue_msg.edit(embed=await send_queue(queue_list, page))
            await queue_msg.remove_reaction(reaction, user)

    else:
        await ctx.send(f"❌ Invalid page number. The queue has {num_pages} page(s).")



@bot.event
async def on_reaction_add(reaction, user):
    if user == bot.user:
        return

    ctx = await bot.get_context(reaction.message)
    if ctx.command and ctx.command.name == "show_queue":
        embed = reaction.message.embeds[0]
        page = int(embed.footer.text.split(" ")[1].split("/")[0])
        queue_list = embed.description.split("\n")

        if reaction.emoji == "⬅️" and page > 1:
            new_page = page - 1
        elif reaction.emoji == "➡️" and page < ((len(queue_list) - 1) // 10 + 1):
            new_page = page + 1
        else:
            return

        await reaction.remove(user)
        await reaction.message.edit(embed=await send_queue(queue_list, new_page))

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

async def display_youtube_search_results(ctx, search_results):
    search_list = "\n".join([f"{idx + 1}. {result['title']}" for idx, result in enumerate(search_results)])
    embed = discord.Embed(title="🎵 YouTube Search Results", description=search_list)
    message = await ctx.send(embed=embed)
    return message

@bot.command(name='youtube', help='Play audio from a YouTube link or search for a video')
async def youtube(ctx, *, query):
    ydl_opts = {
        'format': 'bestaudio/best',
        'noplaylist': True,
        'quiet': True,
    }

    # Check if the URL is a valid YouTube video URL
    if "youtube.com" in query or "youtu.be" in query:
        video_url = query
    else:  # Perform a search
        ydl_opts["default_search"] = "ytsearch10"
        search_message = await ctx.send("🔍 Searching YouTube for videos, one moment...")
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            try:
                info_dict = ydl.extract_info(query, download=False)
                search_results = info_dict["entries"]
                await search_message.delete()  # Delete the search message
                message = await display_youtube_search_results(ctx, search_results)
                await ctx.send("Please type the number of the video you want to play.")

                def check(msg):
                    return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(search_results)

                response = await bot.wait_for("message", timeout=60.0, check=check)
                chosen_video = search_results[int(response.content) - 1]
                video_url = chosen_video["webpage_url"]
            except Exception as e:
                print(f"Error searching YouTube videos: {e}")
                return await ctx.send("❌ Error searching YouTube videos. Please try again.")

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(video_url, download=False)
            audio_url = info_dict['url']
            title = info_dict['title']
            duration = info_dict['duration']
        except Exception as e:
            print(f"Error extracting audio from YouTube video: {e}")
            return await ctx.send("❌ This is not a valid YouTube video link.")

    # Play the extracted audio in the voice channel
    await play_song(ctx, audio_url, f"📺 {title}", duration * 1000)  # Convert seconds to milliseconds


async def display_playlists_page(ctx, page, max_pages, playlists):
    start_idx = page * 10
    end_idx = start_idx + 10
    playlist_list = "\n".join([f"{idx + 1}. {pl.title}" for idx, pl in enumerate(playlists[start_idx:end_idx])])

    embed = discord.Embed(title="🎵 Available Playlists", description=playlist_list)
    embed.set_footer(text=f"Page {page + 1} of {max_pages}")

    message = await ctx.send(embed=embed)

    if max_pages > 1:
        if page > 0:
            await message.add_reaction("⬅️")
        if page < max_pages - 1:
            await message.add_reaction("➡️")
    
    return message

async def show_playlists(ctx):
    playlists = plex.playlists()
    max_pages = math.ceil(len(playlists) / 10)
    current_page = 0
    message = await display_playlists_page(ctx, current_page, max_pages, playlists)

    def check_reaction(reaction, user):
        return user == ctx.author and reaction.message.id == message.id and str(reaction.emoji) in ["⬅️", "➡️"]

    def check(msg):
        return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(playlists)

    while True:
        done, pending = await asyncio.wait([
            bot.wait_for("reaction_add", check=check_reaction),
            bot.wait_for("message", check=check)],
            return_when=asyncio.FIRST_COMPLETED,
            timeout=60.0
        )

        for future in pending:
            future.cancel()

        if not done:
            break

        result = done.pop().result()
        if isinstance(result, tuple):  # Reaction
            reaction, user = result
            await message.remove_reaction(reaction, user)

            if str(reaction.emoji) == "⬅️" and current_page > 0:
                current_page -= 1
            elif str(reaction.emoji) == "➡️" and current_page < max_pages - 1:
                current_page += 1
            else:
                continue

            await message.edit(embed=await display_playlists_page(ctx, current_page, max_pages, playlists))
        else:  # Message
            response = result
            chosen_playlist = playlists[int(response.content) - 1]
            tracks = chosen_playlist.items()
            for track in tracks:
                queue.append((track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration))

            await ctx.send(f"🎵 Loaded {len(tracks)} songs from the '{chosen_playlist.title}' playlist into the queue.")
            if not (ctx.voice_client and ctx.voice_client.is_playing()):
                await play_next_song(ctx)
            break

    await message.clear_reactions()

@bot.command(name='playlist', help='List all playlists and play songs from the chosen playlist')
async def playlist(ctx):
    await show_playlists(ctx)

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

bot.remove_command('help')

@bot.command(name='help', help='Show the help information')
async def help(ctx, *args):
    if args:
        command = bot.get_command(args[0])
        if command:
            embed = discord.Embed(
                title=f"Command: !{command.name}",
                description=command.help,
                color=discord.Color.blue()
            )
            embed.set_footer(text="<> indicates a required argument. [] indicates an optional argument.")
            await ctx.send(embed=embed)
        else:
            await ctx.send(f"❌ Command '{args[0]}' not found.")
    else:
        embed = discord.Embed(
            title="Plex Music Bot Commands",
            description="Here are the available commands:",
            color=discord.Color.blue()
        )
        
        for command in bot.commands:
            embed.add_field(name=f"!{command.name}", value=command.help, inline=False)
        
        embed.set_footer(text="Type !help <command> for more info on a command.")
        await ctx.send(embed=embed)

@bot.event
async def on_command_error(ctx, error):
    if isinstance(error, commands.CommandInvokeError):
        print(f"Error in command '{ctx.command}', {error}")
        await ctx.send("⚠️ Hang tight, I encountered an error so I'm resetting to clear them.")
        await bot.logout()
        os.execv(sys.executable, ['python'] + sys.argv)

bot.run(TOKEN)