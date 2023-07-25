# Use the official Python base image
FROM python:3.11.4-alpine3.18

# Set the working directory
WORKDIR /app

# Copy the requirements file and app folder into the container
COPY requirements.txt /app ./

# Install FFmpeg, build dependencies, app dependencies, and then shrink image by removing no longer needed build dependencies
RUN apk add --no-cache ffmpeg && \
    apk add --no-cache --virtual .pynacl_deps build-base python3-dev libffi-dev make && \
    pip install --no-cache-dir -r requirements.txt && \
    apk del .pynacl_deps build-base python3-dev libffi-dev make

# Expose the port the bot will use (optional)
EXPOSE 32400

# Run the bot script
CMD ["python", "plex_music_bot.py"]

