window.chartInterop = (() => {
    const charts = {};

    function init(canvasId) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        if (charts[canvasId]) {
            charts[canvasId].destroy();
        }

        charts[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Tension (V)',
                    data: [],
                    borderColor: '#3b82f6',
                    backgroundColor: 'rgba(59,130,246,0.08)',
                    borderWidth: 1.5,
                    pointRadius: 0,
                    tension: 0.2,
                    fill: true
                }]
            },
            options: {
                animation: false,
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: ctx => `${ctx.parsed.y.toFixed(5)} V`
                        }
                    }
                },
                scales: {
                    x: {
                        display: false
                    },
                    y: {
                        title: { display: true, text: 'Tension (V)' },
                        ticks: { callback: v => v.toFixed(3) }
                    }
                }
            }
        });
    }

    function update(canvasId, data) {
        const chart = charts[canvasId];
        if (!chart) return;

        chart.data.labels = data.map((_, i) => i);
        chart.data.datasets[0].data = data;
        chart.update('none');
    }

    return { init, update };
})();
