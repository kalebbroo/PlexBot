import io
import requests
import traceback
import random
import yt_dlp
import discord
from discord.ext import commands
from discord import app_commands, Colour, Embed
from youtubesearchpython import VideosSearch, Playlist
from plexapi.server import PlexServer
from config import TOKEN, PLEX_TOKEN, BASEURL
from PIL import Image
import asyncio
import datetime

plex = PlexServer(BASEURL, PLEX_TOKEN)

intents = discord.Intents.all()
client = discord.Client(intents=intents)
tree = app_commands.CommandTree(client)
intents.messages = True
bot = commands.Bot(command_prefix='/', intents=intents)

current_song_title = ""
current_song_duration = 0
song_duration = None
view_instances = {}
play_lock = asyncio.Lock()
queue_buttons_instances = {}


@bot.event
async def on_ready():
    print(f'We have logged in as {bot.user}')
    try:
        synced = await bot.tree.sync()
        print(f"Synced {len(synced)} command(s)")
        await bot.change_presence(activity=discord.Activity(type=discord.ActivityType.listening,
                                                            name="/Play",))
    except Exception as e:
        print(f"Error syncing commands: {e}")


class MusicQueue:
    def __init__(self):
        self.queue = []
        self.playlist_queue = []
        self.message_id = None

    async def add_song(self, song_info, playlist=False):
        if playlist:
            self.playlist_queue.append(song_info)
        else:
            self.queue.append(song_info)

    async def next_song(self):
        if self.playlist_queue:
            return self.playlist_queue.pop(0)
        elif self.queue:
            return self.queue.pop(0)
        else:
            return None

    def is_empty(self):
        return len(self.queue) == 0 and len(self.playlist_queue) == 0
music_queue = MusicQueue()






class MyMusicView(discord.ui.View):
    def __init__(self):
        super().__init__()
        self.add_item(discord.ui.Button(style=discord.ButtonStyle.secondary, label="Pause", custom_id="music_pause_button"))
        self.add_item(discord.ui.Button(style=discord.ButtonStyle.success, label="Play", custom_id="music_play_button"))
        self.add_item(discord.ui.Button(style=discord.ButtonStyle.blurple, label="Skip", custom_id="music_skip_button"))
        self.add_item(discord.ui.Button(style=discord.ButtonStyle.primary, label="Shuffle", custom_id="music_shuffle_button"))
        self.add_item(discord.ui.Button(style=discord.ButtonStyle.danger, label="Kill", custom_id="music_kill_button"))



@bot.event
async def on_interaction(interaction: discord.Interaction):
    if interaction.type == discord.InteractionType.component:
        custom_id = interaction.data["custom_id"]
        match custom_id:
            case "music_pause_button":
                await pause(interaction)
            case "music_play_button":
                await resume(interaction)
            case "music_skip_button":
                await skip(interaction)
            case "music_shuffle_button":
                await interaction.response.defer()
                await shuffle(interaction)
            case "music_kill_button":
                await kill(interaction)
            case "back_button":
                view = queue_buttons_instances.get(interaction.guild.id)
                if view is not None:
                    await view.back_button(interaction)
            case "next_button":
                view = queue_buttons_instances.get(interaction.guild.id)
                if view is not None:
                    await view.next_button(interaction)
            case _:
                pass  # Do nothing for all other cases


        

class EmbedButtons(discord.ui.View):
    def __init__(self, interaction, items, num_pages, generate_embed, shuffle: bool = False):
        super().__init__(timeout=None)
        self.guild_id = interaction.guild.id
        self.page = 1
        self.items = items
        self.num_pages = num_pages
        self.interaction = interaction
        self.generate_embed = generate_embed
        self.shuffle = shuffle

        view_instances[self.guild_id] = self

        self.add_item(discord.ui.Button(label="Back", style=discord.ButtonStyle.blurple, custom_id="back_button"))
        self.add_item(discord.ui.Button(label="Next", style=discord.ButtonStyle.blurple, custom_id="next_button"))

    async def refresh(self):
        self.clear_items()  # Clear old buttons and dropdown
        self.add_item(discord.ui.Button(label="Back", style=discord.ButtonStyle.blurple, custom_id="back_button"))
        self.add_item(discord.ui.Button(label="Next", style=discord.ButtonStyle.blurple, custom_id="next_button"))


    async def on_timeout(self) -> None:
        if self.guild_id in view_instances:
            del view_instances[self.guild_id]

    async def next_button(self, interaction: discord.Interaction):
        if self.page < self.num_pages:
            self.page += 1
            await self.refresh()  # Refresh the view
            await interaction.response.edit_message(embed=await self.generate_embed(self.items, self.page), view=self)

    async def back_button(self, interaction: discord.Interaction):
        if self.page > 1:
            self.page -= 1
            await self.refresh()  # Refresh the view
            await interaction.response.edit_message(embed=await self.generate_embed(self.items, self.page), view=self)






@bot.event
async def on_voice_state_update(member, before, after):
    if before.channel is not None:
        # Check if bot is alone in the voice channel
        await asyncio.sleep(1)
        if bot.user in before.channel.members and len(before.channel.members) == 1:
            # If bot is alone, run the kill function
            print(f"Bot was alone in {before.channel.name} - killing")
            print(f"len(before.channel.members): {len(before.channel.members)}")
            await kill(before.channel)
    if after.channel is not None:
        print(f"Bot joined {after.channel.name}")
        print(f"Number of Users in VC: {len(after.channel.members)}")
        # Check if bot is alone in the voice channel
        await asyncio.sleep(1)
        if bot.user in after.channel.members and len(after.channel.members) == 1:
            # If bot is alone, run the kill function
            print(f"Bot was alone in {after.channel.name} - killing")
            print(f"len(after.channel.members): {len(after.channel.members)}")
            await kill(after.channel)




async def disconnect_after(interaction, music_queue, duration=600):
    print(f"disconnect_after was called")
    await asyncio.sleep(duration)
    voice_client = interaction.guild.voice_client
    if voice_client and not voice_client.is_playing() and not voice_client.is_paused():
        if len(music_queue.queue) == 0:  # Check if there are no songs in the queue
            try:
                if voice_client:
                    await interaction.channel.send("❌ Disconnecting from voice channel due to inactivity.")
                    await voice_client.disconnect()
            except Exception as e:
                print(f"Error during disconnect: {e}")
                await interaction.channel.send("❌ An error occurred while disconnecting. Please try again.")
        else:
            await interaction.channel.send("⚠️ Queue is not empty. Bot will remain connected to the voice channel.")







def safe_attr(obj, *attrs):
    for attr in attrs:
        if isinstance(obj, dict):
            obj = obj.get(attr, None)
        else:
            obj = getattr(obj, attr, None)
        if obj is None:
            return None
    return obj







async def create_embed(track, song_title, formatted_duration, art_file):
    embed = discord.Embed(title="Plex Bot", color=0x00b0f0)
    embed.add_field(name="Now playing", value=song_title, inline=False)
    album_title = safe_attr(track, 'parentTitle') or "Unknown"
    embed.add_field(name="Album title", value=album_title, inline=True)
    track_number = safe_attr(track, 'index') or "Unknown"
    embed.add_field(name="Track number", value=track_number, inline=True)
    embed.add_field(name="Song duration", value=formatted_duration, inline=True)

    thumb_url = safe_attr(track, 'thumbUrl')
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
            embed.set_thumbnail(url="https://images.freeimages.com/fic/images/icons/820/simply_google/256/google_youtube.png")  # Use a default thumbnail if there's an error
    else:
        embed.set_thumbnail(url="https://images.freeimages.com/fic/images/icons/820/simply_google/256/google_youtube.png")  # Use a default thumbnail if thumb_url is None or an empty string

    return embed, art_file

async def delete_old_embed(interaction, music_queue):
        try:
            async for message in interaction.channel.history(limit=5):
                if message.id == music_queue.message_id:
                    await message.delete()  # Delete the old message
                    break
                else:
                    print(f"Failed to delete old embed. If there is no previous embed in this channel this is intentional")
        except Exception as e:
            print(f"Error retrieving or deleting old message: {e}")

async def play_song(interaction, url, song_info, song_duration, track, send_message=True, music_queue=None, play_called=True, play_next=False):
    print("play_song() called")
    view = MyMusicView()
    art_file = None
    if play_called:
        print("play_called set to true returning")
        return
    if music_queue is None:
        music_queue = MusicQueue()

    if interaction.guild.voice_client is None:
        if interaction.user.voice and interaction.user.voice.channel:  # Check if user is in a voice channel
            channel = interaction.user.voice.channel
            voice_client = await channel.connect()
        else:
            await interaction.channel.send("❌ You must be in a voice channel to play music.")
            return
    else:
        voice_client = interaction.guild.voice_client

    if not play_next:
        if voice_client.is_playing() or voice_client.is_paused():
            song_added = await music_queue.add_song((url, song_info, song_duration, track))
            if song_added:  # Check if song was added successfully
                formatted_duration = str(datetime.timedelta(seconds=int(song_duration / 1000)))
                if send_message:
                    embed, art_file = await create_embed(track, song_info, formatted_duration, art_file)
                    try:
                        await interaction.response.send_message(embed=embed, file=art_file if art_file else None, view=view)
                    except discord.errors.InteractionResponded:
                        await interaction.followup.send(embed=embed, file=art_file if art_file else None, view=view, wait=True)
            return

    song_info = await music_queue.next_song() if play_next else (url, song_info, song_duration, track)
    if song_info:  # Check if song_info is not None
        url, song_title, song_duration, track = song_info
        if url == "placeholder":
            song_info = await music_queue.next_song()
            if song_info is None:
                return
            url, song_title, song_duration, track = song_info
        print(f"Playing: {song_title}")
        formatted_duration = str(datetime.timedelta(seconds=int(song_duration / 1000)))
        # Create listening activity with song title
        activity = discord.Activity(type=discord.ActivityType.listening, name=song_title)

        await bot.change_presence(activity=activity)

    music_queue.current_song_duration = song_duration
    music_queue.current_song_title = song_info

    FFMPEG_OPTIONS = {
        'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5',
        'options': '-vn',
    }

    def wrapped_play_next(error):
        coro = play_song(interaction, None, None, None, None, send_message=True, music_queue=music_queue, play_called=False, play_next=True)
        task = asyncio.run_coroutine_threadsafe(coro, bot.loop)
        task.add_done_callback(lambda _: asyncio.run_coroutine_threadsafe(disconnect_after(interaction, music_queue), bot.loop))
        if not music_queue.queue:  # If there's no next song
            # Reset the bot's presence
            asyncio.run_coroutine_threadsafe(bot.change_presence(activity=discord.Activity(type=discord.ActivityType.listening,
                                                            name="/Play")), bot.loop)

    voice_client.play(discord.FFmpegPCMAudio(url, **FFMPEG_OPTIONS), after=wrapped_play_next)

    if send_message:
        await delete_old_embed(interaction, music_queue)
        embed, art_file = await create_embed(track, song_title, formatted_duration, art_file)
        try:
            if art_file is not None:
                message = await interaction.channel.send(embed=embed, file=art_file, view=view)  # Send the new message
            else:
                message = await interaction.channel.send(embed=embed, view=view)  # Send the new message
        except Exception as e:
            print(f"Error sending new embed: {e}")
            message = None

        if message is None:  # if the message was not successfully sent
            print("Failed to send the 'Now playing' embed.")
            return
        else:
            print(f"New 'Now playing' embed sent for song: {song_title}")
            music_queue.message_id = message.id  # Store the message ID










@bot.tree.command(name="play", description="Play a song by title and artist")
@app_commands.describe(query="Song title, artist name, or both")
async def play(interaction: discord.Interaction, query: str):
    global music_queue
    try:
        await interaction.response.defer()
    except discord.errors.NotFound:
        pass
    try:
        print("Received play command:", query)

        if interaction.user.voice is None:
            print("User not in voice channel")
            await interaction.channel.send("❌ You must be in a voice channel to play music.")
            return

        channel = interaction.user.voice.channel
        print("User in voice channel:", channel)

        voice_client = interaction.guild.voice_client
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
                    #print(f"tracks before extend and shuffle: {tracks}")
                    all_tracks.extend(tracks)
                # Shuffle the list of tracks
                random.shuffle(all_tracks)
                # Get the first 20 tracks
                first_20_tracks = all_tracks[:20]
                #print(f"tracks from artist: {first_20_tracks}")
                # Create a list of track names and their indices
                track_list = [f"{idx + 1}. {track.grandparentTitle} - {track.title}" for idx, track in enumerate(first_20_tracks)]
                # Create a discord.Embed object with the track list
                embed = discord.Embed(title="🔍 Found multiple songs for the artist", description="\n".join(track_list), color=0x00b0f0)
                query_msg = await interaction.followup.send(embed=embed, ephemeral=True)
                def check(msg):
                    return msg.author == interaction.user and msg.content.isdigit() and 1 <= int(msg.content) <= len(track_list)
                try:
                    response = await interaction.client.wait_for("message", timeout=60.0, check=check)
                    track = first_20_tracks[int(response.content) - 1]
                except asyncio.TimeoutError:
                    print("No response received")
                    return await interaction.channel.send("❌ No response received. Please try again.")

            else:
                track = matching_tracks[0]

            if track:
                await interaction.channel.send(f"🔍 Found a song for '{query}'.")
                await play_song(interaction, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=True, music_queue=music_queue, play_called=False)

        elif tracks:
             track = tracks[0]
             await interaction.followup.send(f"🔍 Found a song for '{query}'.")
             await play_song(interaction, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=True, music_queue=music_queue, play_called=False)
        else:
            await interaction.channel.send(f"❌ Couldn't find a song for '{query}'.")

    except Exception as e:
        print("Error in play command:")
        traceback.print_exc()
        await interaction.channel.send("❌ An error occurred while processing your request. Please try again.")

           










@bot.tree.command(name="artist", description="search for songs by artist")
@app_commands.describe(artist_name = "Enter Artist Name")
async def artist(interaction: discord.Interaction, artist_name: str):
    global music_queue
    print(f"Searching for songs by '{artist_name}'")
    try:
        await interaction.response.defer()
    except discord.errors.NotFound:
        pass
    
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
            await play_song(interaction, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=send_message, music_queue=music_queue, play_called=False)
            queued_tracks += 1

        await interaction.channel.send(f"🎵 Queued {queued_tracks} songs by {artist_name}.")
    else:
        await interaction.channel.send(f"❌ Couldn't find any songs by '{artist_name}'.")








@bot.tree.command(name='album', description='Queue all songs from the specified album or list albums by an artist')
@app_commands.describe(album_name = "Enter Album Name")
async def album(interaction: discord.Interaction, album_name: str):
    global music_queue
    print(f"Searching for album or artist '{album_name}'")
    try:
        await interaction.response.defer()
    except discord.errors.NotFound:
        pass

    # Search for the album or artist in the Plex library
    music_library = next((section for section in plex.library.sections() if section.type == 'artist'), None)
    if not music_library:
        await interaction.channel.send("❌ Music library not found.")
        return

    album_results = music_library.search(album_name)
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
            await play_song(interaction, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=send_message, music_queue=music_queue, play_called=False)
            queued_tracks += 1

        await interaction.channel.send(f"🎵 Queued {queued_tracks} songs from the album '{album.title}'.")
        
    elif matching_artists:
        artist = matching_artists[0]
        albums = artist.albums()

        # List all the albums from the artist
        album_list = [f"{idx + 1}. {album.title}" for idx, album in enumerate(albums)]
        embed = discord.Embed(title=f"🔍 Found albums for '{artist.title}'", description="\n".join(album_list), color=0x00b0f0)
        await interaction.channel.send(embed=embed)

        def check(msg):
            return msg.author == interaction.user and msg.content.isdigit() and 1 <= int(msg.content) <= len(album_list)

        try:
            # Wait for the user's response with the selected album's number
            response = await bot.wait_for("message", timeout=60.0, check=check)
            selected_album = albums[int(response.content) - 1]
            tracks = selected_album.tracks()

            # Queue all the songs from the selected album
            queued_tracks = 0
            for index, track in enumerate(tracks):
                send_message = index == 0  # Set send_message to True for the first track and False for the rest
                await play_song(interaction, track.getStreamURL(), f"{track.grandparentTitle} - {track.title}", track.duration, track, send_message=send_message, music_queue=music_queue, play_called=False)
                queued_tracks += 1

            await interaction.channel.send(f"🎵 Queued {queued_tracks} songs from the album '{selected_album.title}'.")
            
        except asyncio.TimeoutError:
            return await interaction.channel.send("❌ No response received. Please try again.")
    else:
        await interaction.channel.send(f"❌ Couldn't find any album or artist matching '{album_name}'.")










async def send_queue(queue_list, page):
    start = (page - 1) * 21
    end = start + 21

    embed = discord.Embed(title="🎵 **---------- Current Queue ----------** 🎵", color=0x00b0f0)

    for i in range(start, end, 2):
        if i < len(queue_list):
            song_1 = queue_list[i]
            song_2 = queue_list[i+1] if (i+1) < len(queue_list) else None

            field_value = f"**{i+1}.** {song_1}\n\n"
            if song_2:
                field_value += f"**{i+2}.** {song_2}\n\n"

            embed.add_field(name="\u200b", value=field_value, inline=True)

    num_pages = (len(queue_list) - 1) // 21 + 1
    embed.set_footer(text=f"Page {page}/{num_pages}")

    return embed







@bot.tree.command(name="queue", description="Show the current queue")
@app_commands.describe(page_number = "Enter page number")
async def queue(interaction: discord.Interaction, page_number: int = 1):
    await interaction.response.defer()
    guild_id = interaction.guild.id

    if not music_queue.queue and (interaction.guild.voice_client is None or not interaction.guild.voice_client.is_playing()):
        await interaction.channel.send("❌ There are no songs in the queue.")
        return
    else:
        queue_list = []
        
        if interaction.guild.voice_client.is_playing() and music_queue.current_song_duration is not None:
            current_duration = str(datetime.timedelta(seconds=int(music_queue.current_song_duration/1000)))
            queue_list.append(f"🔊 Currently Playing: {music_queue.current_song_title[1]} ({current_duration})")
            
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
            queue_list.append(f"{title} ({duration})")

        num_pages = (len(queue_list) - 1) // 21 + 1

    if 1 <= page_number <= num_pages:
        if guild_id not in queue_buttons_instances:
            view = EmbedButtons(interaction, queue_list, num_pages, send_queue)
            queue_buttons_instances[guild_id] = view
        else:
            view = queue_buttons_instances[guild_id]
            view.page = page_number
        await view.refresh()
        await interaction.channel.send(embed=await send_queue(queue_list, page_number), view=view)
    else:
        await interaction.channel.send(f"❌ Invalid page number. The queue has {num_pages} page(s).")








async def pause(interaction: discord.Interaction):
    global music_queue
    guild = interaction.guild
    voice_client = guild.voice_client
    
    if voice_client and voice_client.is_playing():
        voice_client.pause()
        await interaction.channel.send(f"⏸️ Paused:")

async def resume(interaction: discord.Interaction):
    global music_queue
    guild = interaction.guild
    voice_client = guild.voice_client

    if voice_client is None:
        await interaction.channel.send("❌ I am not connected to a voice channel.")
        return

    if voice_client.is_playing():
        await interaction.channel.send("❌ I am already playing a song.")
        return

    if voice_client.is_paused():
        voice_client.resume()
        await interaction.channel.send(f"▶️ Resumed playing:")
        return

    if music_queue.queue:
        await play_song(interaction, *music_queue.queue[0], send_message=True, play_called=False)
    else:
        await interaction.channel.send("❌ There are no songs in the queue.")

async def skip(interaction: discord.Interaction):
    global music_queue
    guild = interaction.guild
    voice_client = guild.voice_client

    if voice_client and voice_client.is_playing():
        voice_client.stop()
        await asyncio.sleep(1)
        if music_queue.queue:
            print("Playing next song...")
            url, title, duration, track = music_queue.queue.pop(0)
            music_queue.current_song_title = title
            music_queue.current_song_duration = duration
            await play_song(interaction=interaction, url=url, song_info=title, song_duration=duration, track=track, send_message=True, music_queue=music_queue, play_called=True)
            await interaction.channel.send("⏭ Skipped the current song.")
        else:
            music_queue.current_song_title = None
            music_queue.current_song_duration = None
            await interaction.channel.send("⏹️ The queue is empty. There are no more songs to play.")
    else:
        await interaction.channel.send("❌ There is no song currently playing.")



async def shuffle(interaction: discord.Interaction):
    global music_queue
    if len(music_queue.queue) > 0:
        random.shuffle(music_queue.queue)
        await interaction.channel.send("🔀 Shuffled the current queue.")
    else:
        await interaction.channel.send("❌ There are no songs in the queue to shuffle.")

async def kill(obj):
    if isinstance(obj, discord.Interaction):
        guild = obj.guild
        channel = obj.channel
    elif isinstance(obj, discord.VoiceChannel):
        guild = obj.guild
        channel = obj

    voice_client = guild.voice_client

    if voice_client:
        if voice_client.is_playing() or voice_client.is_paused():
            voice_client.stop()

        music_queue.queue.clear()  # Clear the queue

        message_content = "⏹ Stopped playing music and cleared the queue."
        if isinstance(obj, discord.Interaction):
            await obj.response.send_message(message_content)
        else:
            await channel.send(message_content)
        print("Sent message:", message_content)

        await voice_client.disconnect()
        print("Disconnected voice client")
    else:
        print("Voice client does not exist")
        if isinstance(obj, discord.Interaction):
            await obj.response.send_message("❌ Not connected to a voice channel.")
        else:
            await channel.send("❌ Not connected to a voice channel.")
        print("Sent message: ❌ Not connected to a voice channel.")








@bot.tree.command(name="clear_queue", description="Clear the current queue")
async def clear_queue(interaction):
    global music_queue
    music_queue.queue.clear()
    await interaction.channel.send("🗑️ Cleared the current queue.")



@bot.tree.command(name="remove_song", description="Remove a specific song from the queue")
@app_commands.describe(song_number = "Enter the song number to remove")
async def remove_song(interaction: discord.Interaction, song_number: int):
    global music_queue
    if len(music_queue.queue) >= song_number and song_number > 0:
        removed_song = music_queue.queue.pop(song_number - 1)[1]
        await interaction.channel.send(f"🗑️ Removed song '{removed_song}' from the queue.")
    else:
        await interaction.channel.send("❌ Invalid song number.")








@bot.tree.command(name="playlist", description="List all playlists and play songs from the chosen playlist")
@app_commands.describe(shuffle = "Enter True or False")
@app_commands.describe(playlist_title = "The title of the playlist to be played")
async def playlist(interaction: discord.Interaction, shuffle: bool, playlist_title: str = None):
    await interaction.response.defer()
    if playlist_title:
        await play_playlist_by_title(interaction, music_queue, playlist_title, shuffle)
    else:
        await show_playlists(interaction, music_queue, shuffle)


async def display_playlists_page(interaction):
    embed = discord.Embed(
        title="🎵________Select a Playlist________🎵",
        description="Please choose a playlist from the dropdown menu below to start playing.",
        color=0x00b0f0
    )
    return embed

async def show_playlists(interaction, music_queue, shuffle: bool):
    try:
        playlists = plex.playlists()[5:]
        print(f"Fetched playlists: {playlists}")
        num_pages = (len(playlists) - 1) // 21 + 1
        page = 1

        guild_id = interaction.guild.id
        if guild_id not in playlist_buttons_instances:
            view = CombinedView(interaction, playlists, num_pages, display_playlists_page, shuffle)
            playlist_buttons_instances[guild_id] = view
        else:
            view = playlist_buttons_instances[guild_id]
            view.page = page
            view.shuffle = shuffle
        await view.refresh()

        await interaction.channel.send(embed=await display_playlists_page(interaction), view=view)

    except Exception as e:
        print(f"Error in show_playlists: {e}")
        await interaction.channel.send("🚫 An error occurred while displaying the playlists.")




playlist_buttons_instances = {}




async def play_playlist_by_title(interaction, music_queue, title: str, shuffle: bool):
    playlists = plex.playlists()[5:]
    print(f"Fetched playlists: {playlists}")
    matching_playlists = [playlist for playlist in playlists if playlist.title.lower() == title.lower()]
    if matching_playlists:
        chosen_playlist = matching_playlists[0]
        tracks = chosen_playlist.items()
        if shuffle:
            random.shuffle(tracks)
        for track in tracks:
            artist = track.grandparentTitle if hasattr(track, 'grandparentTitle') else "Unknown Artist"
            await music_queue.add_song((track.getStreamURL(), f"{artist} - {track.title}", track.duration, track))

        await interaction.response.send_message(f"🎵 Loaded {len(tracks)} songs from the '{chosen_playlist.title}' playlist into the queue.")
        if not (interaction.guild.voice_client and (interaction.guild.voice_client.is_playing() or interaction.guild.voice_client.is_paused())):
            first_song = music_queue.queue.pop(0)
            formatted_duration = str(datetime.timedelta(seconds=int(first_song[2] / 1000)))
            await play_song(interaction, *first_song, send_message=True, music_queue=music_queue, play_called=False)

    else:
        await interaction.channel.send(f"❌ Playlist '{title}' not found.")



class PlaylistSelect(discord.ui.Select):
    def __init__(self, playlists, shuffle):
        options = []
        for i, playlist in enumerate(playlists, start=1):
            options.append(discord.SelectOption(label=playlist.title, value=i, description=f"{len(playlist.items())} songs"))
        
        super().__init__(placeholder="Select a playlist to play", options=options)
        self.playlists = playlists
        self.shuffle = shuffle

    async def callback(self, interaction: discord.Interaction):
        selected_playlist = self.playlists[int(self.values[0]) - 1]
        await play_playlist_by_title(interaction, music_queue, selected_playlist.title, self.shuffle)


class CombinedView(discord.ui.View):
    def __init__(self, interaction, items, num_pages, generate_embed, shuffle: bool = False):
        super().__init__(timeout=None)
        self.guild_id = interaction.guild.id
        self.page = 1
        self.items = items
        self.num_pages = num_pages
        self.interaction = interaction
        self.generate_embed = generate_embed
        self.shuffle = shuffle

        playlist_buttons_instances[self.guild_id] = self

        self.add_item(PlaylistSelect(items, shuffle))

    async def refresh(self):
        start_idx = (self.page - 1) * 21
        end_idx = start_idx + 21
        self.clear_items()
        self.add_item(PlaylistSelect(self.items[start_idx:end_idx], self.shuffle))










class YouTubeSearchSelect(discord.ui.Select):
    def __init__(self, options, interaction, music_queue):
        super().__init__(placeholder='Select a video to play...', options=options)
        self.interaction = interaction
        self.music_queue = music_queue

    async def callback(self, interaction: discord.Interaction):
        interaction.response.defer()
        selected_option = self.values[0]
        song_info = await YouTube.get_song_info(selected_option)
        if song_info is None:
            return await interaction.followup.send("❌ This is not a valid YouTube video link.")
        song = (song_info['url'], f"📺 {song_info['title']}", song_info['duration'] * 1000, song_info)
        await play_song(self.interaction, *song, send_message=True, music_queue=self.music_queue, play_called=False)



class YouTube:
    ydl_opts = {
        'format': 'bestaudio/best',
        'socket_timeout': 60,
        'noplaylist': True,
        'quiet': True,
    }

    @staticmethod
    def search(query, playlist=False):
        try:
            search = VideosSearch(query, limit=20)
            search_results = search.result()["result"]
            filtered_results = [result for result in search_results if not result.get("isLive")]
            return [{"title": result.get("title", ""), "webpage_url": result.get("link", ""), "duration": result.get("duration", "")} for result in filtered_results]
        except Exception as e:
            print(f"Error searching YouTube videos: {e}")
            return None

    @staticmethod
    async def get_raw_url(url):
        with yt_dlp.YoutubeDL(YouTube.ydl_opts) as ydl:
            try:
                info_dict = ydl.extract_info(url, download=False)
                return info_dict['url']
            except Exception as e:
                print(f"Error extracting audio from YouTube video: {e}")
                return None

    @staticmethod
    async def get_song_info(entry):
        with yt_dlp.YoutubeDL(YouTube.ydl_opts) as ydl:
            try:
                info_dict = ydl.extract_info(entry, download=False)
                yt_thumbnail = "https://images.freeimages.com/fic/images/icons/820/simply_google/256/google_youtube.png"
                return {'title': info_dict['title'], 'duration': info_dict['duration'], 'thumbnail': info_dict.get('thumbnail', 'yt_thumbnail'), 'url': info_dict['url']}
            except Exception as e:
                print(f"Error extracting info from YouTube video: {e}")
                return None


@bot.tree.command(name='youtube', description='Play audio from a YouTube link or search for a video or playlist')
@app_commands.describe(search = "Search for a YouTube Video or Playlist")
@app_commands.describe(video_url = "Enter a YouTube Video URL")
@app_commands.describe(playlist_url = "Enter a YouTube Playlist URL")
async def youtube(interaction: discord.Interaction, search: str = None, video_url: str = None, playlist_url: str = None):
    await interaction.response.defer()
    if search:
        # Handle search functionality
        search_results = YouTube.search(search)
        if search_results is None:
            return await interaction.channel.send("❌ Error searching videos. None were found, Please try searching again.")
        # Create options for the SelectMenu
        options = [
            discord.SelectOption(label=result['title'], value=result['webpage_url'])
            for result in search_results
        ]
        # Create the SelectMenu
        select_menu = YouTubeSearchSelect(options, interaction, music_queue)
        # Create a View and add the SelectMenu to it
        view = discord.ui.View()
        view.add_item(select_menu)
        # Send the SelectMenu
        embed = discord.Embed(
        title="🔍 YouTube Search Results 🔍",
        description=f"Here are the search results for **{search}**. Please select a video from the dropdown menu below to play it.",
        color=discord.Color.blue()
        )
        embed.set_footer(text="Plex Bot | YouTube Search")
        await interaction.channel.send(embed=embed, view=view)


    elif video_url:
        # Handle video URL functionality
        raw_url = await YouTube.get_raw_url(video_url)
        if raw_url is None:
            return await interaction.followup.send("❌ This is not a valid YouTube video link.")
        song_info = await YouTube.get_song_info(video_url)
        if song_info is None:
            return await interaction.followup.send("❌ This is not a valid YouTube video link.")
        song = (raw_url, f"📺 {song_info['title']}", song_info['duration'] * 1000, song_info)
        await play_song(interaction, *song, send_message=True, music_queue=music_queue, play_called=False)
    elif playlist_url:
        # Handle playlist URL functionality
        await interaction.channel.send("⌛ Playing first soing & Processing playlist, this may take several moments depending on the length of the playlist.")
        playlist = Playlist.get(playlist_url)
        playlist_entries = playlist["videos"]
        total_songs = len(playlist_entries)
        print(playlist.keys())
        if playlist_entries:
            # Process and add songs in the playlist in the background
            bot.loop.create_task(process_playlist(interaction, playlist_entries, music_queue))
            # Add a placeholder song to the queue
            placeholder_song = ("placeholder", f"📺 Playlist: {playlist['info']['title']} ({total_songs} songs)", 0, {})
            print(f"{playlist['info']['title']}")
            await music_queue.add_song(placeholder_song)
        else:
            interaction.channel.send("❌ Error searching YouTube playlist. Please try again.")
    else:
        await interaction.channel.send("❌ Please enter a search query, a video URL, or a playlist URL after the command.")



async def process_playlist(interaction, playlist_entries, music_queue):
    for entry in playlist_entries:
        song_info = await YouTube.get_song_info(entry['link'])
        if song_info is None:
            await interaction.channel.send(f"❌ Error extracting audio from video {entry['link']}. Skipping.")
            continue
        song = (song_info['url'], f"📺 {song_info['title']}", song_info['duration'] * 1000, song_info)
        await music_queue.add_song(song, playlist=True)
        if len(music_queue.playlist_queue) == 1:
            await play_song(interaction, *song, send_message=True, music_queue=music_queue, play_called=False)
            music_queue.playlist_queue.pop(0)
    await interaction.channel.send(f"✅ Finished processing playlist.")









commands = {
    "play": "Play a song or playlist. Usage: /play <song/playlist name>",
    "playlist": "Toggle loop mode. Usage: /playlist",
    "pause": "Pause the current song. Usage: button only",
    "resume": "Resume the paused song. Usage: button only",
    "kill": "Stop the current song and clear the queue. Usage: /kill or button",
    "skip": "Skip the current song. Usage: button only",
    "queue": "Show the current music queue. Usage: /queue",
    "clear_queue": "Clear the current music queue. Usage: /clear_queue",
    "shuffle": "Toggle shuffle mode. Usage: button only",
    "remove": "Remove a song from the queue. Usage: /remove <index>",
    "help": "Show the help information. Usage: /help <command>"
}

@bot.tree.command(name='help', description='Show available commands')
@app_commands.describe(command='The command to get help for')
#@app_commands.choices(command=[Choice(name=name, value=name) for name in commands.keys()])
async def help(interaction: discord.Interaction, command: str = None):
    embed = Embed(
        title="Plex Music Bot Commands",
        description="Here are the available commands:",
        color=Colour.brand_green()
    )
    
    for command, description in commands.items():
        embed.add_field(name=f"/{command}", value=description, inline=False)
    
    embed.set_footer(text="Type /help <command> for more info on a command.")
    await interaction.channel.send(embed=embed)



bot.run(TOKEN)
