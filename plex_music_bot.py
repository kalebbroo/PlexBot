import os
import io
import requests
import random
import yt_dlp
import discord
from discord.ext import commands
from plexapi.myplex import MyPlexAccount
from config import TOKEN, PLEX_USERNAME, PLEX_PASSWORD, SERVER
from PIL import Image
import concurrent.futures
import asyncio
import datetime
import math
import sys

current_song_title = ""
current_song_duration = 0

account = MyPlexAccount(PLEX_USERNAME, PLEX_PASSWORD)
plex = account.resource(SERVER).connect()

intents = discord.Intents.all()
intents.messages = True

bot = commands.Bot(command_prefix='!', intents=intents)

queue = []

# Define a lock to prevent multiple instances of play_next_song() from running at the same time
play_lock = asyncio.Lock()



async def disconnect_after(ctx, duration=120):
    await asyncio.sleep(duration)
    voice_client = ctx.voice_client
    if voice_client and not voice_client.is_playing() and not voice_client.is_paused():
        if len(queue) == 0:  # Check if there are no songs in the queue
            try:
                if voice_client:
                    await ctx.send("❌ Disconnecting from voice channel due to inactivity.")
                    await voice_client.disconnect()
            except Exception as e:  # Add exception handling here
                print(f"Error during disconnect: {e}")
                await ctx.send("❌ An error occurred while disconnecting. Please try again.")
        else:
            await ctx.send("⚠️ Queue is not empty. Bot will remain connected to the voice channel.")





async def on_voice_state_update(member, before, after):
    if before.channel and before.channel != after.channel:  # User left a voice channel
        vc = None
        for voice_client in bot.voice_clients:
            if voice_client.channel == before.channel:
                vc = voice_client
                break

        if vc and len(vc.channel.members) == 1:  # The bot is the only one in the channel
            print("All users left the voice channel. Stopping the song, clearing the queue, and disconnecting.")
            vc.stop()
            queue.clear()
            await vc.disconnect()

            # Assuming bot_channel_id is the ID of the text channel where you want to send the message
            bot_channel = bot.get_channel(368593145816154124)
            await bot_channel.send(f"😢 Everyone left me... Shutting down until I feel loved again.")










async def play_next_song(ctx):
    global current_song_duration, current_song_title, queue

    # Acquire the lock before running the function
    async with play_lock:
        if not queue:
            # Call disconnect_after here when the queue is empty
            if current_song_duration is not None:
                print("Last song in the queue finished. Waiting for 2 minutes before disconnecting...")
                await disconnect_after(ctx, int(current_song_duration / 1000) + 2 * 60)


        # Stop the current song if it is still playing
        if ctx.voice_client and ctx.voice_client.is_playing():
            ctx.voice_client.stop()

        # Update the current song's title and duration
        song_info = queue.pop(0)
        url = song_info[0]
        current_song_title = song_info[1]
        current_song_duration = song_info[2]
        track = song_info[3]

        FFMPEG_OPTIONS = {
            'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5',
            'options': '-vn',
        }

        if current_song_duration is not None:
            duration = str(datetime.timedelta(seconds=int(current_song_duration / 1000)))
        else:
            duration = "Unknown"

        embed = discord.Embed(title="Plex Bot", color=0x00b0f0)
        embed.add_field(name="Now playing", value=current_song_title, inline=False)
        embed.add_field(name="Album title", value=safe_attr(track, 'parentTitle') or "Unknown", inline=True)
        embed.add_field(name="Song duration", value=duration, inline=True)
        embed.add_field(name="Track number", value=safe_attr(track, 'index') or "Unknown", inline=True)

        # Add album artwork to the embed
        if safe_attr(track, 'thumbUrl'):
            img_stream = requests.get(safe_attr(track, 'thumbUrl'), stream=True).raw
            img = Image.open(img_stream)

            # Resize the image
            max_size = (500, 500)
            img.thumbnail(max_size)

            # Save the resized image to a BytesIO object
            resized_img = io.BytesIO()
            img.save(resized_img, format='PNG')
            resized_img.seek(0)

            art_file = discord.File(resized_img, filename="image0.png")
            embed.set_thumbnail(url="attachment://image0.png")
            await ctx.send(embed=embed, file=art_file)
        else:
            await ctx.send(embed=embed)

        try:
            voice_client = ctx.voice_client
            if not voice_client:
                voice_client = await ctx.author.voice.channel.connect()
            voice_client.play(discord.FFmpegPCMAudio(url, **FFMPEG_OPTIONS), after=lambda e: play_next(e, ctx))
            print("Song started playing")
        except Exception as e:
            await ctx.send(f"❌ Error playing '{current_song_title}': {str(e)}")



def play_next(e, ctx):
    global queue
    if queue:
        asyncio.run_coroutine_threadsafe(play_next_song(ctx), bot.loop)




def safe_attr(obj, *attrs):
    for attr in attrs:
        if isinstance(obj, dict):
            obj = obj.get(attr, None)
        else:
            obj = getattr(obj, attr, None)
        if obj is None:
            return None
    return obj






async def play_song(ctx, url, song_info, song_duration, track, send_message=True, queue_song=False):
    global current_song_title
    global current_song_duration

    # Connect to the voice channel if not already connected
    if ctx.voice_client is None:
        channel = ctx.author.voice.channel if ctx.author.voice else None
        if channel:
            voice_client = await channel.connect()
        else:
            await ctx.send("❌ You must be in a voice channel to play music.")
            return
    else:
        voice_client = ctx.voice_client

    # If the bot is already playing a song, add the new song to the queue
    if voice_client.is_playing() or voice_client.is_paused():
        queue.append((url, song_info, song_duration, track))
        if send_message and song_duration is not None:
            duration_seconds = int(song_duration / 1000)
            formatted_duration = str(datetime.timedelta(seconds=duration_seconds))
            embed = discord.Embed(title=f"🎵 Added to queue: {song_info} ({formatted_duration})", color=0x00b0f0)
            await ctx.send(embed=embed)
        elif send_message:
            embed = discord.Embed(title=f"🎵 Added to queue: {song_info}", color=0x00b0f0)
            await ctx.send(embed=embed)

        # If the queue is not empty, then play_next_song() will be called after the current song finishes,
        # so there's no need to do anything else here
        if not queue:
            return
    else:
        # Play the song
        FFMPEG_OPTIONS = {
            'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5',
            'options': '-vn',
        }
        
        # Define a callback to play the next song once this song has finished playing
        def play_next(e):
            asyncio.run_coroutine_threadsafe(play_next_song(ctx), bot.loop)

        voice_client.play(discord.FFmpegPCMAudio(url, **FFMPEG_OPTIONS), after=play_next)

        # Update the current song's title and duration
        current_song_title = song_info
        current_song_duration = song_duration
        if song_duration is not None:
            duration_seconds = int(song_duration / 1000)
            formatted_duration = str(datetime.timedelta(seconds=duration_seconds))
        else:
            formatted_duration = "Unknown"

        if not queue_song and send_message:
            
            # Send an embed with the now playing information
            embed = discord.Embed(title="Plex Bot", color=0x00b0f0)
            embed.add_field(name="Now playing", value=song_info, inline=False)
            album_title = safe_attr(track, 'parentTitle') or "Unknown"
            embed.add_field(name="Album title", value=album_title, inline=True)
            track_number = safe_attr(track, 'index') or "Unknown"
            embed.add_field(name="Track number", value=track_number, inline=True)
            embed.add_field(name="Song duration", value=formatted_duration, inline=True)

            
            # Add album artwork to the embed
            if safe_attr(track, 'thumbUrl'):
                img_stream = requests.get(safe_attr(track, 'thumbUrl'), stream=True).raw
                img = Image.open(img_stream)

                # Resize the image
                max_size = (500, 500)
                img.thumbnail(max_size)

                # Save the resized image to a BytesIO object
                resized_img = io.BytesIO()
                img.save(resized_img, format='PNG')
                resized_img.seek(0)

                art_file = discord.File(resized_img, filename="image0.png")
                embed.set_thumbnail(url="attachment://image0.png")
                await ctx.send(embed=embed)
                print("play_song() called")






@bot.command(name='play', help='Play a song by title and artist')
async def play(ctx, *, query):
    try:
        print("Received play command:", query)

        channel = ctx.author.voice.channel
        if not channel:
            print("User not in voice channel")
            await ctx.send("❌ You must be in a voice channel to play music.")
            return
        print("User in voice channel:", channel)

        voice_client = ctx.voice_client
        if voice_client is None:
            voice_client = await channel.connect()
        elif voice_client.channel != channel:
            await voice_client.move_to(channel)
        print("Connected to voice channel:", channel)



        results = plex.search(query)
        print(f"search results: {results}")
        



        
        tracks = [item for item in results if item.type == 'track']
        print(f"Track results:", len(tracks))

        matching_tracks = []
        if any(item.type == 'artist' for item in results):
            print("Tracks not found searching for artists")

            # Get the artist object from the search results
            artist_search = [item for item in results if item.type == 'artist']
            if artist_search:
                artist = artist_search[0]

                # Fetch the albums by the artist
                albums = artist.albums()

                # Initialize an empty list to store the tracks
                all_tracks = []

                # For each album, fetch the tracks and add them to the list
                for album in albums:
                    tracks = album.tracks()
                    print(f"tracks before extend and shuffle: {tracks}")
                    all_tracks.extend(tracks)

                # Shuffle the list of tracks
                random.shuffle(all_tracks)

                # Get the first 20 tracks
                first_20_tracks = all_tracks[:20]
                print(f"tracks from artist: {first_20_tracks}")

                # Create a list of track names and their indices
                track_list = [f"{idx + 1}. {track.grandparentTitle} - {track.title}" for idx, track in enumerate(first_20_tracks)]

                # Create a discord.Embed object with the track list
                embed = discord.Embed(title="🔍 Found multiple songs for the artist", description="\n".join(track_list), color=0x00b0f0)
                query_msg = await ctx.send(embed=embed)

                def check(msg):
                    return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(track_list)

                try:
                    response = await bot.wait_for("message", timeout=60.0, check=check)
                    track = first_20_tracks[int(response.content) - 1]
                except asyncio.TimeoutError:
                    print("No response received")
                    return await ctx.send("❌ No response received. Please try again.")
            else:
                track = matching_tracks[0]

        

            await ctx.send(f"🔍 Found a song for '{query}'.")

            is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()

            if not is_playing:
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, queue_song=False)
            else:
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=True, queue_song=True)

        elif any(all(word.lower() in track.title.lower() for word in query.split()) for track in tracks):

            for track in tracks:
                if all(word.lower() in track.title.lower() for word in query.split()):
                    matching_tracks.append(track)

            if not matching_tracks:
                matching_tracks = tracks[:20]

            if len(matching_tracks) > 1:
                track_list = [f"{idx + 1}. {track.grandparentTitle} - {track.title}" for idx, track in enumerate(matching_tracks)]
                embed = discord.Embed(title="🔍 Found multiple songs for your query", description="\n".join(track_list), color=0x00b0f0)
                query_msg = await ctx.send(embed=embed)

                def check(msg):
                    return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(track_list)

                try:
                    response = await bot.wait_for("message", timeout=60.0, check=check)
                    track = matching_tracks[int(response.content) - 1]
                except asyncio.TimeoutError:
                    print("No response received")
                    return await ctx.send("❌ No response received. Please try again.")
            else:
                track = matching_tracks[0]

        

            await ctx.send(f"🔍 Found a song for '{query}'.")

            is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()

            if not is_playing:
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, queue_song=False)
            else:
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=True, queue_song=True)
        else:
            print("No matching songs found")
            await ctx.send(f"❌ Couldn't find a song for '{query}'.")

        if not ctx.voice_client.is_playing():
            await play_next_song(ctx)
    except Exception as e:
        print("Error during play command:", e)
        await ctx.send("❌ An error occurred while processing your request. Please try again.")







@bot.command(name='artist', help='Queue all songs by the specified artist')
async def artist(ctx, *, artist_name):
    print(f"Searching for songs by '{artist_name}'")
    
    # Search for the artist in the Plex library
    music_library = next((section for section in plex.library.sections() if section.type == 'artist'), None)
    artist_results = music_library.search(artist_name) if music_library else []

    if artist_results:
        artist = artist_results[0]
        tracks = artist.tracks()

        # Queue all the songs by the artist
        queued_tracks = 0
        for track in tracks:
            is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()
            await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=False, queue_song=is_playing)
            queued_tracks += 1

        await ctx.send(f"🎵 Queued {queued_tracks} songs by {artist_name}.")
    else:
        await ctx.send(f"❌ Couldn't find any songs by '{artist_name}'.")








@bot.command(name='album', help='Queue all songs from the specified album or list albums by an artist')
async def album(ctx, *, query):
    print(f"Searching for album or artist '{query}'")

    # Search for the album or artist in the Plex library
    music_library = next((section for section in plex.library.sections() if section.type == 'artist'), None)
    if not music_library:
        await ctx.send("❌ Music library not found.")
        return

    album_results = music_library.search(query)
    matching_albums = [result for result in album_results if result.type == 'album']
    matching_artists = [result for result in album_results if result.type == 'artist']

    if matching_albums:
        print(f"Albums found: {', '.join(album.title for album in matching_albums)}")
        album = matching_albums[0]
        tracks = album.tracks()

        # Queue all the songs from the album
        queued_tracks = 0
        for track in tracks:
            is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()
            await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=False, queue_song=is_playing)
            queued_tracks += 1

        await ctx.send(f"🎵 Queued {queued_tracks} songs from the album '{album.title}'.")
    elif matching_artists:
        artist = matching_artists[0]
        albums = artist.albums()

        # List all the albums from the artist
        album_list = [f"{idx + 1}. {album.title}" for idx, album in enumerate(albums)]
        embed = discord.Embed(title=f"🔍 Found albums for '{artist.title}'", description="\n".join(album_list), color=0x00b0f0)
        query_msg = await ctx.send(embed=embed)

        def check(msg):
            return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(album_list)

        try:
            # Wait for the user's response with the selected album's number
            response = await bot.wait_for("message", timeout=60.0, check=check)
            selected_album = albums[int(response.content) - 1]
            tracks = selected_album.tracks()

            # Queue all the songs from the selected album
            queued_tracks = 0
            for track in tracks:
                is_playing = ctx.voice_client is not None and ctx.voice_client.is_playing()
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=False, queue_song=is_playing)
                queued_tracks += 1

            await ctx.send(f"🎵 Queued {queued_tracks} songs from the album '{selected_album.title}'.")
        except asyncio.TimeoutError:
            return await ctx.send("❌ No response received. Please try again.")
    else:
        await ctx.send(f"❌ Couldn't find any album or artist matching '{query}'.")









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
        for idx, item in enumerate(queue, start=1):
            url, title, duration = item[:3]
            if len(item) > 3:
                track = item[3]
            else:
                track = None

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
        current_song = current_song_title  # get the current song title
        voice_client.stop()
        await asyncio.sleep(1)
        if queue:
            print("Playing next song...")
            url, title, duration, track = queue.pop(0)
            await play_song(ctx, url, title, duration, track, send_message=True, queue_song=False)
            await ctx.send("⏭ Skipped the current song.")
        else:
            await ctx.send("⏹️ The queue is empty. There are no more songs to play.")
    else:
        await ctx.send("❌ There is no song currently playing.")

    # Check if the skipped song was the current song
    if queue and queue[0][1] == current_song:
        print("Playing next song...")
        url, title, duration, track = queue.pop(0)
        await play_song(ctx, url, title, duration, track, send_message=True, queue_song=False)





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





async def display_choices(ctx, title, choices, max_choices_per_page=10):
    num_pages = math.ceil(len(choices) / max_choices_per_page)

    async def send_page(page):
        start_idx = page * max_choices_per_page
        end_idx = start_idx + max_choices_per_page
        choice_list = "\n".join([f"{idx + 1}. {choice}" for idx, choice in enumerate(choices[start_idx:end_idx])])

        embed = discord.Embed(title=title, description=choice_list)
        embed.set_footer(text=f"Page {page + 1} of {num_pages}")

        return embed

    message = await ctx.send(embed=await send_page(0))

    if num_pages > 1:
        await message.add_reaction("⬅️")
        await message.add_reaction("➡️")

    return message, num_pages




@bot.event
async def on_command_error(ctx, error):
    if isinstance(error, commands.CommandNotFound):
        await ctx.send("❌ Invalid command. Use !help to see the available commands.")
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send("❌ Missing required argument. Use !help to see the command usage.")
    else:
        await ctx.send(f"❌ Error: {error}")









async def display_youtube_search_results(ctx, search_results):
    search_list = "\n".join(
        [
            f"{idx + 1}. {result['title']} [{str(datetime.timedelta(seconds=result['duration']))}]"
            for idx, result in enumerate(search_results)
        ]
    )
    embed = discord.Embed(title="🎵 YouTube Search Results", description=search_list)
    message = await ctx.send(embed=embed)
    return message







def search_youtube(query, ydl_opts):
    ydl_opts['default_search'] = 'ytsearch20'
    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(query, download=False)
            search_results = info_dict["entries"]
            filtered_results = [result for result in search_results if 'is_live' in result and not result["is_live"]]
            return [{"title": result["title"], "webpage_url": result["webpage_url"], "duration": result["duration"]} for result in filtered_results]
        except Exception as e:
            print(f"Error searching YouTube videos: {e}")
            return None



def search_youtube_playlist(playlist_id, ydl_opts, start=1, end=None):
    ydl_opts['noplaylist'] = False
    ydl_opts['playlist-start'] = start
    if end:
        ydl_opts['playlist-end'] = end

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(f'https://www.youtube.com/playlist?list={playlist_id}', download=False)
            return [{"title": entry["title"], "webpage_url": entry["webpage_url"]} for entry in info_dict["entries"]]
        except Exception as e:
            print(f"Error searching YouTube playlist: {e}")
            return None


        





@bot.command(name='youtube', help='Play audio from a YouTube link or search for a video or playlist')
async def youtube(ctx, *, query):
    ydl_opts = {
        'format': 'bestaudio/best',
        'socket_timeout': 30,
        'noplaylist': True,
        'quiet': True,
    }

    loop = asyncio.get_event_loop()

    if "youtube.com" in query or "youtu.be" in query:
        video_url = query
    else:
        search_message = await ctx.send("🔍 Searching YouTube for videos, one moment...")
        
        with concurrent.futures.ThreadPoolExecutor() as pool:
            search_results = await loop.run_in_executor(pool, search_youtube, query, ydl_opts)
        
        if search_results is None:
            await search_message.delete()
            return await ctx.send("❌ Error searching YouTube videos. Please try again.")
        else:
            await search_message.delete()
            message = await display_youtube_search_results(ctx, search_results)
            await ctx.send("Please type the number of the video you want to play.")

            def check(msg):
                return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(search_results)

            response = await bot.wait_for("message", timeout=60.0, check=check)
            chosen_video = search_results[int(response.content) - 1]
            video_url = chosen_video["webpage_url"]

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(video_url, download=False)
            if 'entries' in info_dict:  # It's a playlist
                for entry in info_dict['entries']:
                    audio_url = entry['url']
                    title = entry['title']
                    duration = entry['duration']
                    song = (audio_url, f"📺 {title}", duration * 1000)
                    queue.append(song)
            else:
                audio_url = info_dict['url']
                title = info_dict['title']
                duration = info_dict['duration']
                song = (audio_url, f"📺 {title}", duration * 1000)
                queue.append(song)
        except Exception as e:
            print(f"Error extracting audio from YouTube video: {e}")
            return await ctx.send("❌ This is not a valid YouTube video link.")

    await ctx.send(f"🎵 Added {title} to the queue.")

    if not (ctx.voice_client and (ctx.voice_client.is_playing() or ctx.voice_client.is_paused())):
        await play_song(ctx, *queue[0], send_message=True, queue_song=True, track=None)






    





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
                artist = track.grandparentTitle if hasattr(track, 'grandparentTitle') else "Unknown Artist"
                queue.append((track.getStreamURL(), f"{artist} - {track.title}", track.duration, track))

            await ctx.send(f"🎵 Loaded {len(tracks)} songs from the '{chosen_playlist.title}' playlist into the queue.")
            if not (ctx.voice_client and (ctx.voice_client.is_playing() or ctx.voice_client.is_paused())):
                await play_song(ctx, *queue[0], send_message=True)

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


bot.run(TOKEN)