// Change Sets page (#1570): the undo journal list, before/after details
// and the rollback action with confirmation and conflict report. jQuery +
// Bootstrap modal idioms follow alertSchedule.js; the shared confirmation
// modal is window.showConfirmationModal (confirmation.js).
(function () {
    function refreshTable() {
        $.get(window.changeSetsUrls.table, { limit: 200 }, function (html) {
            $('#changeSetsTableContainer').html(html);
        });
    }

    function showDetails(changeSetId) {
        $.get(window.changeSetsUrls.details, { id: changeSetId }, function (html) {
            var $modal = $(
                '<div class="modal fade" id="changeSetDetailsModal" tabindex="-1">' +
                '<div class="modal-dialog modal-xl"><div class="modal-content"></div></div></div>');

            $modal.find('.modal-content').html(html);
            $('#changeSetDetailsContainer').empty().append($modal);
            $modal.modal('show');

            $modal.on('click', '#changeSetRollbackFromDetails', function () {
                $modal.modal('hide');
                confirmRollback(changeSetId);
            });
        });
    }

    function confirmRollback(changeSetId) {
        // Dry-run first: the confirmation dialog shows the restore plan and
        // the conflict report before anything is applied.
        $.post(window.changeSetsUrls.rollback, { id: changeSetId, dryRun: true }, function (preview) {
            if (preview.error) {
                window.showConfirmationModal('Rollback failed', preview.error, null);
                return;
            }

            var conflicts = (preview.entries || []).filter(function (e) { return (e.conflicts || []).length > 0; });
            var hasConflicts = conflicts.length > 0;
            var body = 'Roll back this change set (' + (preview.status || 'completed') + ')?<br/>' +
                (preview.message || '') +
                (hasConflicts
                    ? '<div class="alert alert-warning mt-2 mb-0 small">' +
                      conflicts.map(function (e) {
                          return '<div><strong>' + e.path + '</strong></div>' +
                              e.conflicts.map(function (c) { return '<div>' + c + '</div>'; }).join('');
                      }).join('') +
                      '</div>' +
                      '<div class="mt-1">Entities changed after the change set was recorded. ' +
                      'Forcing the rollback DELIBERATELY overrides those changes with the recorded ' +
                      'before-images.</div>'
                    : '');

            window.showConfirmationModal(
                hasConflicts ? 'Rollback has conflicts' : 'Rollback change set',
                body,
                function () {
                    $.post(window.changeSetsUrls.rollback, { id: changeSetId, force: hasConflicts }, function (result) {
                        var text = result.error
                            ? result.error
                            : (result.message || 'Rolled back.') +
                              (result.status === 'partial' ? ' Retry to restore the failed entries.' : '');

                        window.showConfirmationModal(
                            result.error || result.status === 'partial' ? 'Rollback partial' : 'Rollback complete',
                            text, null);

                        refreshTable();
                    });
                },
                null,
                hasConflicts ? 'Force rollback' : 'Rollback');
        });
    }

    $(document).ready(function () {
        refreshTable();

        $(document).on('click', '.change-set-details-btn', function () {
            showDetails($(this).data('change-set-id'));
        });

        $(document).on('click', '.change-set-rollback-btn', function () {
            confirmRollback($(this).data('change-set-id'));
        });
    });
}());
