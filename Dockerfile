# Use the official Python base image
FROM python:3.11.2

# Install FFmpeg
RUN apt-get update && \
    apt-get install -y ffmpeg

# Set the working directory
WORKDIR /app

# Copy the requirements file into the container
COPY requirements.txt .

# Install the dependencies
RUN pip install --no-cache-dir -r requirements.txt

# Copy the entire project into the container
COPY . .

# Expose the port the bot will use (optional)
 EXPOSE 32400

# Run the bot script
CMD ["python", "plex_music_bot.py"]

