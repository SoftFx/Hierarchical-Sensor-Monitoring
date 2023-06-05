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

    $(`#${modalId}`).modal({
        backdrop: 'static'
    });
    $(`#${modalId}`).modal('show');
}

window.hideConfirmationModal = function () {
    $(`#${modalId}`).modal('hide');
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
    $(`#${modalCancelButton}`).html(name);
    $(`#${modalCancelButton}`).off('click').on('click', () => {
        hideConfirmationModal();

        if (cancelButtonAction != undefined)
            cancelButtonAction();
    });
}