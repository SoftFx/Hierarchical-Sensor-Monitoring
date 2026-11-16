// Sensor move (#1722): the tree's "Move" modal and the sensor page's alias
// removal. The server decides everything (HomeController.MoveSensors ->
// SensorMoveAdministrationService); this file only collects the input, shows
// the dry-run prediction and the per-sensor outcome.

function antiForgeryToken() {
    return $('input[name="__RequestVerificationToken"]').first().val();
}

function escapeHtml(text) {
    return $('<div>').text(text ?? '').html();
}

const outcomeLabels = {
    moved: 'moves',
    merged: 'merges',
    unchanged: 'already there',
    conflict: 'conflict',
    notFound: 'not found',
    forbidden: 'no rights',
    invalidPath: 'invalid path',
    failed: 'failed',
};

function outcomeBadge(outcome) {
    const ok = outcome === 'moved' || outcome === 'merged' || outcome === 'unchanged';
    const merge = outcome === 'merged';

    return `<span class="badge ${merge ? 'bg-warning text-dark' : ok ? 'bg-success' : 'bg-danger'}">${escapeHtml(outcomeLabels[outcome] ?? outcome)}</span>`;
}

function moveRequest(dryRun) {
    return $.ajax({
        url: moveSensorsAction,
        type: 'POST',
        contentType: 'application/json',
        headers: { RequestVerificationToken: antiForgeryToken() },
        data: JSON.stringify({
            sensorIds: $('#moveSensors_ids').val().split(',').filter(id => id !== ''),
            productId: $('#moveSensors_product').val(),
            destination: $('#moveSensors_destination').val(),
            merge: $('#moveSensors_merge').is(':checked'),
            dryRun: dryRun,
        }),
    });
}

function failureText(xhr) {
    return xhr.responseJSON?.error ?? xhr.responseText ?? 'The request failed.';
}

function renderPreview(preview) {
    const rows = (preview.sensors ?? []).map(item => {
        const notes = [];

        if (item.otherSensorId)
            notes.push(item.outcome === 'merged'
                ? `absorbs sensor <code>${escapeHtml(item.otherSensorId)}</code> (it is removed)`
                : `held by sensor <code>${escapeHtml(item.otherSensorId)}</code>`);
        if (item.displacesAliasOf)
            notes.push(`takes over the alias of sensor <code>${escapeHtml(item.displacesAliasOf)}</code>`);
        if (item.createdNodes?.length)
            notes.push(`creates ${item.createdNodes.map(escapeHtml).join(', ')}`);
        if (item.templatesDetached?.length)
            notes.push(`alerts removed: ${item.templatesDetached.map(t => escapeHtml(t.name)).join(', ')}`);
        if (item.templatesApplied?.length)
            notes.push(`alerts added: ${item.templatesApplied.map(t => escapeHtml(t.name)).join(', ')}`);
        if (item.settingsChanged?.length)
            notes.push(item.settingsChanged.map(s => `${escapeHtml(s.name)}: ${escapeHtml(s.before)} → ${escapeHtml(s.after)}`).join('; '));
        if (item.error)
            notes.push(`<span class="text-danger">${escapeHtml(item.error)}</span>`);

        return `<tr>
                    <td class="text-break">${escapeHtml(item.oldPath ?? item.sensorId)}<br/>→ ${escapeHtml(item.newPath ?? '')}</td>
                    <td>${outcomeBadge(item.outcome)}</td>
                    <td class="small">${notes.join('<br/>')}</td>
                </tr>`;
    });

    $('#moveSensors_preview').html(
        `<table class="table table-sm align-middle">
            <thead><tr><th>Sensor</th><th>Outcome</th><th>Details</th></tr></thead>
            <tbody>${rows.join('')}</tbody>
        </table>`);
}

function renderResult(response) {
    const results = response.results ?? [];
    const lines = results
        .filter(r => !['moved', 'merged', 'unchanged'].includes(r.outcome))
        .map(r => `${escapeHtml(r.oldPath ?? r.sensorId)}: ${escapeHtml(r.error ?? outcomeLabels[r.outcome] ?? r.outcome)}`);
    const unchanged = results.filter(r => r.outcome === 'unchanged').length;

    let message = `Moved ${response.moved} sensor(s).`;
    if (unchanged)
        message += ` ${unchanged} already at the destination.`;
    if (lines.length)
        message += `<br/><span style="color: red">${lines.join('<br/>')}</span>`;

    showToast(message);
}

window.openMoveSensorsModal = function (ids) {
    $.ajax({
        url: getMoveSensorsModalAction,
        type: 'GET',
        cache: false,
        data: $.param({ ids: ids }, true),
    }).done(html => {
        $('#moveSensors_partial').html(html);

        const buttons = $('#moveSensors_previewBtn, #moveSensors_moveBtn');
        const busy = request => {
            buttons.prop('disabled', true);
            return request.always(() => buttons.prop('disabled', false));
        };

        $('#moveSensors_previewBtn').on('click', () => {
            busy(moveRequest(true))
                .done(renderPreview)
                .fail(xhr => $('#moveSensors_preview').html(`<div class="text-danger">${escapeHtml(failureText(xhr))}</div>`));
        });

        $('#moveSensors_moveBtn').on('click', () => {
            const merge = $('#moveSensors_merge').is(':checked');

            const run = () => busy(moveRequest(false))
                .done(response => {
                    $('#moveSensors_modal').modal('hide');
                    renderResult(response);
                    updateTreeTimer();
                })
                .fail(xhr => $('#moveSensors_preview').html(`<div class="text-danger">${escapeHtml(failureText(xhr))}</div>`));

            if (merge && !confirm('Merging removes the sensor that holds the destination path and cannot be undone. Continue?'))
                return;

            run();
        });

        $('#moveSensors_modal').modal('show');
    }).fail(xhr => showToast(escapeHtml(failureText(xhr))));
};

window.removeSensorAlias = function (button) {
    const { sensor: sensorId, product: productId, path, row: rowId } = button.dataset;

    if (!confirm(`Stop routing values sent to '${path}' to this sensor? The next value there creates a new sensor.`))
        return;

    $.ajax({
        url: removeSensorAliasAction,
        type: 'POST',
        data: { sensorId: sensorId, productId: productId, path: path, __RequestVerificationToken: antiForgeryToken() },
    }).done(() => {
        const block = $(`#${rowId}`).closest('[id^="sensorAliases_"]');
        $(`#${rowId}`).remove();
        if (block.find('tr').length === 0)
            block.remove();
        showToast(`Alias '${escapeHtml(path)}' removed.`);
    }).fail(xhr => showToast(escapeHtml(failureText(xhr))));
};
