document.addEventListener('DOMContentLoaded', () => {
    const homeView = document.getElementById('home-view');
    const detailsView = document.getElementById('details-view');
    const backToHomeBtn = document.getElementById('back-to-home');
    let hls = null;
    let currentMovieData = null; 
    function showHomeView() {
        detailsView.classList.add('hidden');
        homeView.classList.remove('hidden');
        stopAndDestroyPlayer();
    }
    function showDetailsView() {
        homeView.classList.add('hidden');
        detailsView.classList.remove('hidden');
    }
    backToHomeBtn.addEventListener('click', (e) => {
        e.preventDefault();
        showHomeView();
    });
    window.initializeHomePage = function (homeJson) {
        console.log("Nhận dữ liệu trang chủ từ C#");
        const data = JSON.parse(homeJson);
        document.getElementById('loading-indicator').classList.add('hidden');
        document.getElementById('newly-updated-section').classList.remove('hidden');
        const grid = document.getElementById('newly-updated-grid');
        grid.innerHTML = ''; 

        data.newlyUpdated.forEach(movie => {
            const card = createMovieCard(movie);
            grid.appendChild(card);
        });
    }
    window.displayMovieDetails = function (detailsJson) {
        currentMovieData = JSON.parse(detailsJson);
        document.getElementById('movie-breadcrumb-title').textContent = currentMovieData.Title;
        document.getElementById('details-title').textContent = currentMovieData.Title;
        document.getElementById('details-status').textContent = currentMovieData.Status;
        document.getElementById('details-genre').textContent = currentMovieData.Genre;
        document.getElementById('details-year').textContent = currentMovieData.Year;
        document.getElementById('details-synopsis').textContent = currentMovieData.Synopsis;
        document.getElementById('player-poster').src = currentMovieData.PosterUrl;
        const episodeList = document.getElementById('episode-list');
        episodeList.innerHTML = '';
        currentMovieData.Episodes.forEach((ep, index) => {
            const epButton = document.createElement('button');
            epButton.className = 'episode-btn';
            epButton.textContent = ep.Label;
            epButton.dataset.episodeId = ep.Id;
            if (index === 0) {
                epButton.classList.add('active');
            }
            epButton.addEventListener('click', () => {
                document.querySelectorAll('.episode-btn.active').forEach(btn => btn.classList.remove('active'));
                epButton.classList.add('active');
                requestStreamUrl(currentMovieData.Id, ep.Id);
            });
            episodeList.appendChild(epButton);
        });

        showDetailsView();
    }
    window.playVideoStream = function (streamUrl) {
        console.log(`Bắt đầu phát stream: ${streamUrl}`);
        const videoContainer = document.getElementById('video-player-container');

        stopAndDestroyPlayer(); 
        videoContainer.innerHTML = ''; 

        const video = document.createElement('video');
        video.id = 'movie-player';
        video.controls = true;
        video.autoplay = true;
        videoContainer.appendChild(video);

        if (Hls.isSupported()) {
            hls = new Hls();
            hls.loadSource(streamUrl);
            hls.attachMedia(video);
            hls.on(Hls.Events.MANIFEST_PARSED, () => video.play());
        } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
            video.src = streamUrl;
            video.addEventListener('loadedmetadata', () => video.play());
        }
    }
    function createMovieCard(movie) {
        const card = document.createElement('div');
        card.className = 'movie-card';
        card.dataset.movieId = movie.Id;

        card.innerHTML = `
            <img src="${movie.PosterUrl}" alt="${movie.Title}">
            <div class="movie-episodes">${movie.EpisodeInfo}</div>
            <div class="movie-info">
                <h3 class="movie-title">${movie.Title}</h3>
            </div>
        `;

        card.addEventListener('click', () => {
            const movieId = card.dataset.movieId;
            console.log(`Yêu cầu chi tiết phim có ID: ${movieId}`);
            window.chrome.webview.postMessage({
                action: 'getMovieDetails',
                movieId: movieId
            });
        });
        return card;
    }

    function requestStreamUrl(movieId, episodeId) {
        console.log(`Yêu cầu stream cho phim ${movieId}, tập ${episodeId}`);
        window.chrome.webview.postMessage({
            action: 'getStreamUrl',
            movieId: movieId,
            episodeId: episodeId
        });
    }

    function stopAndDestroyPlayer() {
        if (hls) {
            hls.destroy();
            hls = null;
        }
        const videoContainer = document.getElementById('video-player-container');
        videoContainer.innerHTML = `
            <div class="player-placeholder">
                 <img id="player-poster" src="${currentMovieData?.PosterUrl || ''}" alt="Poster phim"/>
                 <div class="play-button-overlay">▶</div>
            </div>`;
    }
});