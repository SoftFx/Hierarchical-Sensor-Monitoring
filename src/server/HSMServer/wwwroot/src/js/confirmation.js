var modalId = "confirmation_modal";
var modalTitleId = "confirmation_title";
var modalBodyId = "confirmation_body";
var modalOkButton = "confirmation_okButton";
var modalCancelButton = "confirmation_cancelButton";


window.showConfirmationModal = function (title, body, okButtonAction, cancelButtonAction = undefined, okButton = "OK", cancelButton = "Cancel") {
    setConfirmationModalTitle(title);
    setConfirmationModalBody(body);
    setConfirmationModalOkButton(okButtonAction, okButton);
    setConfirmationModalCancelButton(cancelButtonAction, cancelButton);

    const el = document.getElementById(modalId);
    const modal = bootstrap.Modal.getOrCreateInstance(el, { backdrop: 'static' });
    modal.show();
}

window.hideConfirmationModal = function () {
    const el = document.getElementById(modalId);
    const modal = bootstrap.Modal.getInstance(el);
    if (modal) modal.hide();
}


function setConfirmationModalTitle(title) {
    $(`#${modalTitleId}`).empty();
    $(`#${modalTitleId}`).append(title);
}

function setConfirmationModalBody(viewData) {
    $(`#${modalBodyId}`).html(viewData);
}

function setConfirmationModalOkButton(okButtonAction, name) {
    $(`#${modalOkButton}`).html(name);
    $(`#${modalOkButton}`).off('click').on('click', () => {
        hideConfirmationModal();
        okButtonAction();
    });
}

function setConfirmationModalCancelButton(cancelButtonAction, name) {
    var $cancelButton = $(`#${modalCancelButton}`);
    $cancelButton.off('click');

    // A falsy label HIDES the button — acknowledgment-only dialogs (#1401:
    // the "saved with warnings" modal has nothing to cancel, the save already
    // persisted, and a visible Cancel would strand the user on the edit form
    // of a stored template). Every other caller keeps its label.
    if (!name) {
        $cancelButton.addClass('d-none');
        return;
    }

    $cancelButton.removeClass('d-none');
    $cancelButton.html(name);
    $cancelButton.on('click', () => {
        hideConfirmationModal();

        if (cancelButtonAction != undefined)
            cancelButtonAction();
    });
}