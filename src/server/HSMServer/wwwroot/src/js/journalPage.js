// The unified Journal surface (#1588): the global Journal page (filters,
// table, details, rollback with the dry-run-backed confirmation) and the
// shared per-node-tab rollback action. jQuery + Bootstrap modal idioms
// follow alertSchedule.js; the shared confirmation modal is
// window.showConfirmationModal (confirmation.js).
(function () {
    function esc(value) {
        return $('<div>').text(value == null ? '' : String(value)).html();
    }

    function antiForgeryToken() {
        return $('input[name="__RequestVerificationToken"]').first().val();
    }

    function rollbackUrl() {
        return (window.journalUrls && window.journalUrls.rollback) || '/Journal/Rollback';
    }

    // === The shared rollback-by-record action (global page + per-node tab) ===

    // Dry-run first: the confirmation dialog shows the restore plan and the
    // conflict report before anything is applied.
    function confirmRollback(recordId) {
        $.post(rollbackUrl(),
            { id: recordId, dryRun: true, __RequestVerificationToken: antiForgeryToken() },
            function (preview) {
                if (preview.error) {
                    window.showConfirmationModal('Rollback failed', esc(preview.error), null);
                    return;
                }

                var conflicts = (preview.entries || []).filter(function (e) { return (e.conflicts || []).length > 0; });
                var hasConflicts = conflicts.length > 0;
                var body = 'Roll back this record\u2019s request (' + esc(preview.status || 'completed') + ')?<br/>' +
                    esc(preview.message || '') +
                    (hasConflicts
                        ? '<div class="alert alert-warning mt-2 mb-0 small">' +
                          conflicts.map(function (e) {
                              return '<div><strong>' + esc(e.path) + '</strong></div>' +
                                  e.conflicts.map(function (c) { return '<div>' + esc(c) + '</div>'; }).join('');
                          }).join('') +
                          '</div>' +
                          '<div class="mt-1">Entities changed after the write was recorded. Forcing the rollback ' +
                          'DELIBERATELY overrides those changes with the recorded before-images.</div>'
                        : '');

                window.showConfirmationModal(
                    hasConflicts ? 'Rollback has conflicts' : 'Rollback journal record',
                    body,
                    function () {
                        $.post(rollbackUrl(),
                            { id: recordId, force: hasConflicts, __RequestVerificationToken: antiForgeryToken() },
                            function (result) {
                                var text = result.error
                                    ? esc(result.error)
                                    : esc(result.message || 'Rolled back.') +
                                      (result.status === 'partial' ? ' Retry to restore the failed entries.' : '');

                                window.showConfirmationModal(
                                    result.error || result.status === 'partial' ? 'Rollback partial' : 'Rollback complete',
                                    text, null);

                                refreshTable();
                                refreshNodeTabs();
                            });
                    },
                    null,
                    hasConflicts ? 'Force rollback' : 'Rollback');
            });
    }

    $(document).on('click', '.journal-rollback-btn, .journal-record-rollback', function () {
        confirmRollback($(this).data('record-id'));
    });

    // === The global Journal page ===

    // datetime-local values are naive LOCAL wall-clock strings; the server
    // filters in UTC, so the instant is converted before it travels.
    function toUtcIso(value) {
        return value ? new Date(value).toISOString() : null;
    }

    function filterParams() {
        return {
            author: $('#journalFilterAuthor').val() || null,
            kind: $('#journalFilterKind').val() || null,
            productId: $('#journalFilterProduct').val() || null,
            fromUtc: toUtcIso($('#journalFilterFrom').val()),
            toUtc: toUtcIso($('#journalFilterTo').val()),
            limit: 200,
        };
    }

    function refreshTable() {
        if (document.getElementById('journalTableContainer') === null)
            return;

        $.get(window.journalUrls.table, filterParams(), function (html) {
            $('#journalTableContainer').html(html);
        });
    }

    // The per-node Journal tabs after a rollback: the server-side DataTable
    // rows are re-fetched so an already-rolled-back group stops offering the
    // Rollback button.
    function refreshNodeTabs() {
        $('table[id^="journal_table_"]').each(function () {
            if ($.fn.DataTable.isDataTable(this))
                $(this).DataTable().ajax.reload(null, false);
        });
    }

    function showDetails(recordId) {
        $.get(window.journalUrls.details, { id: recordId }, function (html) {
            var $modal = $(
                '<div class="modal fade" id="journalDetailsModal" tabindex="-1">' +
                '<div class="modal-dialog modal-xl"><div class="modal-content"></div></div></div>');

            $modal.find('.modal-content').html(html);
            $('#journalDetailsContainer').empty().append($modal);
            $modal.modal('show');
        });
    }

    $(document).ready(function () {
        if (document.getElementById('journalTableContainer') === null)
            return;

        refreshTable();

        $('#journalFilterApply').on('click', refreshTable);

        $(document).on('click', '.journal-details-btn', function () {
            showDetails($(this).data('record-id'));
        });
    });
}());
