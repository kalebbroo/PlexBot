import os
import io
import requests
import traceback
import random
import yt_dlp
import discord
from discord.ext import commands
#from plexapi.myplex import MyPlexAccount
# If you want to use the old login method uncomment above line and comment the line below this.
from plexapi.server import PlexServer
from config import TOKEN, PLEX_USERNAME, PLEX_PASSWORD, SERVER, PLEX_TOKEN, BASEURL, CHANNEL_ID
from PIL import Image
import concurrent.futures
import functools
import asyncio
import datetime
import math
import sys

# I have switched to using url and plex token method for logging in.
# If using username and password method comment out the below line and uncomment the ones below that.
plex = PlexServer(BASEURL, PLEX_TOKEN)
#account = MyPlexAccount(PLEX_USERNAME, PLEX_PASSWORD)
#plex = account.resource(SERVER).connect()

intents = discord.Intents.all()
intents.messages = True
bot = commands.Bot(command_prefix='!', intents=intents)

current_song_title = ""
current_song_duration = 0
song_duration = None


# Define a lock to prevent multiple instances of play_next_song() from running at the same time
play_lock = asyncio.Lock()


class MusicQueue:
    def __init__(self):
        self.queue = []

    async def add_song(self, song_info):
        self.queue.append(song_info)

    async def next_song(self):
        if not self.queue:
            return None
        return self.queue.pop(0)

    def is_empty(self):
        return len(self.queue) == 0

music_queue = MusicQueue()





@bot.event
async def on_ready():
    print(f'We have logged in as {bot.user}')
    bot.loop.create_task(auto_disconnect())

async def auto_disconnect():
    await bot.wait_until_ready()
    idle_timeout = 120  # seconds

    while not bot.is_closed():
        for guild in bot.guilds:
            voice_client = guild.voice_client
            if voice_client and voice_client.channel:  # Check if the bot is in a voice channel
                # Disconnect if no other users are in the voice channel
                other_users = [member for member in voice_client.channel.members if not member.bot]
                if not other_users:
                    print(f'Disconnecting from voice channel {voice_client.channel} in guild {guild} (no other users)')
                    await voice_client.disconnect()
                    music_queue.queue.clear()
                else:
                    if not voice_client.source:  # Check if a song is playing or paused
                        user = await bot.fetch_user(bot.user.id)
                        ctx = await bot.get_context(user)  # Get the Context object
                        if music_queue.queue:  # If there are songs in the queue, play the first one
                            print(f'Playing a song in guild {guild}')
                            await play_song(ctx, *music_queue.queue[0], send_message=True, music_queue=music_queue)
                        else:  # If there are no songs in the queue, wait `idle_timeout` seconds and then disconnect
                            print(f'Waiting {idle_timeout} seconds before disconnecting in guild {guild}')
                            await asyncio.sleep(idle_timeout)
                            if not voice_client.source:
                                print(f'Disconnecting from voice channel {voice_client.channel} in guild {guild} (no songs in queue)')
                                await voice_client.disconnect()
        await asyncio.sleep(30)  # Check every 30 seconds











async def disconnect_after(ctx, music_queue, duration=120):
    print(f"disconnect_after was called")
    await asyncio.sleep(duration)
    voice_client = ctx.voice_client
    if voice_client and not voice_client.is_playing() and not voice_client.is_paused():
        if len(music_queue.queue) == 0:  # Check if there are no songs in the queue
            try:
                if voice_client:
                    await ctx.send("❌ Disconnecting from voice channel due to inactivity.")
                    await voice_client.disconnect()
            except Exception as e:  # Add exception handling here
                print(f"Error during disconnect: {e}")
                await ctx.send("❌ An error occurred while disconnecting. Please try again.")
        else:
            await ctx.send("⚠️ Queue is not empty. Bot will remain connected to the voice channel.")






def safe_attr(obj, *attrs):
    for attr in attrs:
        if isinstance(obj, dict):
            obj = obj.get(attr, None)
        else:
            obj = getattr(obj, attr, None)
        if obj is None:
            return None
    return obj



async def play_song(ctx, url, song_info, song_duration, track, send_message=True, music_queue=None, play_called=True, play_next=False):
    print("play_song() called")
    if play_called:
        print("play_called set to true returning")
        return
    if music_queue is None:
        music_queue = MusicQueue()

    if ctx.voice_client is None:
        channel = ctx.author.voice.channel if ctx.author.voice and ctx.author.voice.channel else None
        if channel:
            voice_client = await channel.connect()
        else:
            await ctx.send("❌ You must be in a voice channel to play music.")
            return
    else:
                voice_client = ctx.voice_client

    if not play_next:
        if voice_client.is_playing() or voice_client.is_paused():
            await music_queue.add_song((url, song_info, song_duration, track))
            formatted_duration = str(datetime.timedelta(seconds=int(song_duration / 1000)))
            if send_message:
                embed = discord.Embed(title=f"🎵 Added to queue: {song_info} ({formatted_duration})", color=0x00b0f0)
                await ctx.send(embed=embed)
            return

    song_info = await music_queue.next_song() if play_next else (url, song_info, song_duration, track)
    if song_info:
        url, song_title, song_duration, track = song_info
        print(f"Playing: {song_title}")
        formatted_duration = str(datetime.timedelta(seconds=int(song_duration / 1000)))

    music_queue.current_song_duration = song_duration
    music_queue.current_song_title = song_info

    FFMPEG_OPTIONS = {
        'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5',
        'options': '-vn',
    }

    def wrapped_play_next(error):
        coro = play_song(ctx, None, None, None, None, send_message=True, music_queue=music_queue, play_called=False, play_next=True)
        task = asyncio.run_coroutine_threadsafe(coro, bot.loop)
        task.add_done_callback(lambda _: asyncio.run_coroutine_threadsafe(disconnect_after(ctx, music_queue), bot.loop))


    voice_client.play(discord.FFmpegPCMAudio(url, **FFMPEG_OPTIONS), after=wrapped_play_next)

    if send_message:
        embed = discord.Embed(title="Plex Bot", color=0x00b0f0)
        embed.add_field(name="Now playing", value=song_title, inline=False)
        album_title = safe_attr(track, 'parentTitle') or "Unknown"
        embed.add_field(name="Album title", value=album_title, inline=True)
        track_number = safe_attr(track, 'index') or "Unknown"
        embed.add_field(name="Track number", value=track_number, inline=True)
        embed.add_field(name="Song duration", value=formatted_duration, inline=True)

        thumb_url = safe_attr(track, 'thumbUrl')
        art_file = None
        if thumb_url:
            try:
                img_stream = requests.get(thumb_url, stream=True).raw
                img = Image.open(img_stream)

                max_size = (500, 500)
                img.thumbnail(max_size)

                resized_img = io.BytesIO()
                img.save(resized_img, format='PNG')
                resized_img.seek(0)

                art_file = discord.File(resized_img, filename="image0.png")
                embed.set_thumbnail(url="attachment://image0.png")
            except Exception as e:
                print(f"Error retrieving or processing album artwork: {e}")

        if art_file:
            await ctx.send(embed=embed, file=art_file)
        else:
            await ctx.send(embed=embed)







@bot.command(name='play', help='Play a song by title and artist')
async def play(ctx, *, query):
    global music_queue
    try:
        print("Received play command:", query)

        if ctx.author.voice is None:
            print("User not in voice channel")
            await ctx.send("❌ You must be in a voice channel to play music.")
            return

        channel = ctx.author.voice.channel
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

            if track:
                await ctx.send(f"🔍 Found a song for '{query}'.")
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=True, music_queue=music_queue, play_called=False)
        elif tracks:
             track = tracks[0]
             await ctx.send(f"🔍 Found a song for '{query}'.")
             await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=True, music_queue=music_queue, play_called=False)
        else:
            print("No matching songs found")
            await ctx.send(f"❌ Couldn't find a song for '{query}'.")

    except Exception as e:
        print("Error in play command:")
        traceback.print_exc()
        await ctx.send("❌ An error occurred while processing your request. Please try again.")









@bot.command(name='artist', help='Queue all songs by the specified artist')
async def artist(ctx, *, artist_name):
    global music_queue
    print(f"Searching for songs by '{artist_name}'")
    
    # Search for the artist in the Plex library
    music_library = next((section for section in plex.library.sections() if section.type == 'artist'), None)
    artist_results = music_library.search(artist_name) if music_library else []

    if artist_results:
        artist = artist_results[0]
        tracks = artist.tracks()

        # Queue all the songs by the artist
        queued_tracks = 0
        for index, track in enumerate(tracks):
            send_message = index == 0  # Set send_message to True for the first track and False for the rest
            await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=send_message, music_queue=music_queue, play_called=False)
            queued_tracks += 1


        await ctx.send(f"🎵 Queued {queued_tracks} songs by {artist_name}.")
    else:
        await ctx.send(f"❌ Couldn't find any songs by '{artist_name}'.")








@bot.command(name='album', help='Queue all songs from the specified album or list albums by an artist')
async def album(ctx, *, query):
    global music_queue
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
        for index, track in enumerate(tracks):
            send_message = index == 0  # Set send_message to True for the first track and False for the rest
            await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=send_message, music_queue=music_queue, play_called=False)
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
            for index, track in enumerate(tracks):
                send_message = index == 0  # Set send_message to True for the first track and False for the rest
                await play_song(ctx, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=send_message, music_queue=music_queue, play_called=False)
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






@bot.command(name='queue', help='Show the current queue')
async def queue(ctx, page: int = 1):

    if not music_queue.queue and (ctx.voice_client is None or not ctx.voice_client.is_playing()):
        await ctx.send("❌ There are no songs in the queue.")
        return
    else:
        if ctx.voice_client.is_playing() and music_queue.current_song_duration is not None:
            current_duration = str(datetime.timedelta(seconds=int(music_queue.current_song_duration/1000)))
            queue_list = [f"🔊 Currently Playing: {music_queue.current_song_title[1]} ({current_duration})"]
        else:
            queue_list = []

        for idx, item in enumerate(music_queue.queue, start=1):
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
    global music_queue

    if user == bot.user:
        return

    ctx = await bot.get_context(reaction.message)
    if ctx.command and ctx.command.name == "queue":
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
    global music_queue
    voice_client = ctx.voice_client

    if voice_client and voice_client.is_playing():
        current_song = music_queue.current_song_title
        voice_client.stop()
        await asyncio.sleep(1)
        if music_queue.queue:
            print("Playing next song...")
            url, title, duration, track = music_queue.queue.pop(0)
            music_queue.current_song_title = title
            music_queue.current_song_duration = duration
            await play_song(ctx, url, title, duration, track, send_message=True, music_queue=music_queue, play_called=True)
            await ctx.send("⏭ Skipped the current song.")
        else:
            music_queue.current_song_title = None
            music_queue.current_song_duration = None
            await ctx.send("⏹️ The queue is empty. There are no more songs to play.")
    else:
        await ctx.send("❌ There is no song currently playing.")







@bot.command(name='clear_queue', help='Clear the current queue')
async def clear_queue(ctx):
    global music_queue
    music_queue.queue.clear()
    await ctx.send("🗑️ Cleared the current queue.")




@bot.command(name='remove_song', help='Remove a specific song from the queue')
async def remove_song(ctx, *, song_number: int):
    global music_queue
    if len(music_queue.queue) >= song_number and song_number > 0:
        removed_song = music_queue.queue.pop(song_number - 1)[1]
        await ctx.send(f"🗑️ Removed song '{removed_song}' from the queue.")
    else:
        await ctx.send("❌ Invalid song number.")




@bot.command(name='shuffle', help='Shuffle the current queue')
async def shuffle(ctx):
    global music_queue
    if len(music_queue.queue) > 0:
        random.shuffle(music_queue.queue)
        await ctx.send("🔀 Shuffled the current queue.")
    else:
        await ctx.send("❌ There are no songs in the queue to shuffle.")




@bot.command(name='resume', help='Resume playing the current song or start playing the first song in the queue')
async def resume(ctx):
    global music_queue
    voice_client = ctx.voice_client

    if voice_client is None:
        await ctx.send("❌ I am not connected to a voice channel.")
        return

    if voice_client.is_playing():
        await ctx.send("❌ I am already playing a song.")
        return

    if voice_client.is_paused():
        voice_client.resume()
        await ctx.send(f"▶️ Resumed playing: {music_queue.current_song_title}")
        return

    if music_queue.queue:
        await play_song(ctx, *music_queue.queue[0], send_message=True, play_called=False)
    else:
        await ctx.send("❌ There are no songs in the queue.")







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
            filtered_results = [result for result in search_results if not result.get("is_live", False)]
            return [{"title": result["title"], "webpage_url": result["webpage_url"], "duration": result["duration"]} for result in filtered_results]
        except Exception as e:
            print(f"Error searching YouTube videos: {e}")
            return None



def search_youtube_playlist(playlist_id, ydl_opts, start=1, end=None, max_songs=30):
    ydl_opts['noplaylist'] = False
    ydl_opts['playlist-start'] = start
    if end:
        ydl_opts['playlist-end'] = end

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(f'https://www.youtube.com/playlist?list={playlist_id}', download=False)
            entries = info_dict.get('entries', [])[:max_songs]  # limit the number of songs here

            # filter out unavailable and live videos
            filtered_entries = []
            for entry in entries:
                if entry and not entry.get('is_live', False):
                    webpage_url = entry.get('webpage_url')
                    title = entry.get('title')
                    filtered_entries.append({"title": title, "webpage_url": webpage_url})

            return filtered_entries
        except Exception as e:
            print(f"Error searching YouTube playlist: {e}")
            return None










class Downloader:
    ydl_opts = {
        'format': 'bestaudio/best',
        'socket_timeout': 30,
        'noplaylist': True,
        'quiet': True,
    }

    @classmethod
    async def get_raw_url(cls, url, ydl_opts=None):
        ydl_opts = ydl_opts or cls.ydl_opts

        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            try:
                info_dict = ydl.extract_info(url, download=False)
                audio_url = info_dict['url']
                return audio_url
            except Exception as e:
                print(f"Error extracting audio from YouTube video: {e}")
                return None




async def process_playlist_entry(entry, ydl_opts):
    video_url = entry["webpage_url"]
    raw_url = await Downloader.get_raw_url(video_url, ydl_opts)

    if raw_url is None:
        return None

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(video_url, download=False)
            title = info_dict['title']
            duration = info_dict['duration']
        except Exception as e:
            print(f"❌ Error extracting info from YouTube video: {e}")
            return None

    song = (raw_url, f"📺 {title}", duration * 1000, {'title': title, 'duration': duration, 'parentTitle': 'YouTube', 'index': 'Unknown', 'thumbUrl': None})
    return song






@bot.command(name='youtube', help='Play audio from a YouTube link or search for a video or playlist')
async def youtube(ctx, *, query):
    ydl_opts = {
        'format': 'bestaudio/best',
        'socket_timeout': 30,
        'noplaylist': True,
        'quiet': True,
    }

    loop = asyncio.get_event_loop()

    # Check if the query is a playlist link
    if "youtube.com/playlist?list=" in query:
        playlist_id = query.split("list=")[-1]

        await ctx.send("⌛ Processing playlist, this may take several moments.")

        with concurrent.futures.ThreadPoolExecutor() as pool:
            playlist_entries = await loop.run_in_executor(pool, search_youtube_playlist, playlist_id, ydl_opts, 1, 30)  # include 1 and 30 as start and end parameters

        if len(playlist_entries) == 30:
            await ctx.send("ℹ️ The playlist song limit reached. Only the first 30 songs will be added to the queue.")

        if playlist_entries:
            # Get the first entry in the playlist
            first_entry = playlist_entries[0]
            first_song = await process_playlist_entry(first_entry, ydl_opts)

            if first_song is None:
                await ctx.send(f"❌ Error extracting audio from video {first_entry['webpage_url']}. Skipping.")
                return

            await play_song(ctx, *first_song, send_message=True, music_queue=music_queue, play_called=False)

            # Add the rest of the playlist to the queue
            for entry in playlist_entries[1:]:
                song = await process_playlist_entry(entry, ydl_opts)

                if song is None:
                    await ctx.send(f"❌ Error extracting audio from video {video_url}. Skipping.")
                    continue

                await music_queue.add_song(song)

            # Send message after all songs are added to the queue
            await ctx.send(f"✅ Added {len(playlist_entries)} songs to the queue.")

        else:
            await ctx.send("❌ Error searching YouTube playlist. Please try again.")
        return

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

    raw_url = await Downloader.get_raw_url(video_url, ydl_opts)

    if raw_url is None:
        return await ctx.send("❌ This is not a valid YouTube video link.")

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        try:
            info_dict = ydl.extract_info(video_url, download=False)
            title = info_dict['title']
            duration = info_dict['duration']
        except Exception as e:
            print(f"Error extracting info from YouTube video: {e}")
            return await ctx.send("❌ This is not a valid YouTube video link.")

    song = (raw_url, f"📺 {title}", duration * 1000, {'title': title, 'duration': duration, 'parentTitle': 'YouTube', 'index': 'Unknown', 'thumbUrl': None})

    if not (ctx.voice_client and (ctx.voice_client.is_playing() or ctx.voice_client.is_paused())):
        await play_song(ctx, *song, send_message=True, music_queue=music_queue, play_called=False)
    else:
        await music_queue.add_song(song)
        await ctx.send(f"🎵 Added {title} to the queue.")

    return











@bot.command(name='playlist', help='List all playlists and play songs from the chosen playlist')
async def playlist(ctx):
    await show_playlists(ctx, music_queue)

    





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







async def show_playlists(ctx, music_queue):
    playlists = plex.playlists()
    max_pages = math.ceil(len(playlists) / 10)
    current_page = 0
    message = await display_playlists_page(ctx, current_page, max_pages, playlists)

    def check_reaction(reaction, user):
        return user == ctx.author and reaction.message.id == message.id and str(reaction.emoji) in ["⬅️", "➡️"]

    def check(msg):
        return msg.author == ctx.author and msg.content.isdigit() and 1 <= int(msg.content) <= len(playlists)

    while True:
        reaction_task = asyncio.create_task(bot.wait_for("reaction_add", check=check_reaction))
        message_task = asyncio.create_task(bot.wait_for("message", check=check))

        done, pending = await asyncio.wait(
            [reaction_task, message_task],
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
            random.shuffle(tracks)  # Shuffle the tracks before adding them to the queue
            for track in tracks:
                artist = track.grandparentTitle if hasattr(track, 'grandparentTitle') else "Unknown Artist"
                await music_queue.add_song((track.getStreamURL(), f"{artist} - {track.title}", track.duration, track))

            await ctx.send(f"🎵 Loaded {len(tracks)} songs from the '{chosen_playlist.title}' playlist into the queue.")
            if not (ctx.voice_client and (ctx.voice_client.is_playing() or ctx.voice_client.is_paused())):
                first_song = music_queue.queue.pop(0)
                formatted_duration = str(datetime.timedelta(seconds=int(first_song[2] / 1000)))
                await play_song(ctx, *first_song, send_message=True, music_queue=music_queue, play_called=False)


            await message.clear_reactions()














@bot.command(name='kill', help='Stop playing music and disconnect from the voice channel')
async def stop(ctx):
    voice_client = ctx.voice_client

    if voice_client:
        if voice_client.is_playing() or voice_client.is_paused():
            voice_client.stop()

        music_queue.queue.clear()  # Clear the queue

        await ctx.send("⏹ Stopped playing music and cleared the queue.")
        await voice_client.disconnect()
    else:
        await ctx.send("❌ Not connected to a voice channel.")







@bot.event
async def on_command_error(ctx, error):
    if isinstance(error, commands.CommandNotFound):
        await ctx.send("❌ Invalid command. Use !help to see the available commands.")
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send("❌ Missing required argument. Use !help to see the command usage.")
    else:
        await ctx.send(f"❌ Error: {error}")




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