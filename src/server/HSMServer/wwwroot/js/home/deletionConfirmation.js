﻿var modalId = "deletionConfirmation_modal";
var modalTitleId = "deletionConfirmation_title";
var modalBodyId = "deletionConfirmation_body";
var modalOkButton = "deletionConfirmation_okButton";


function showDeletionConfirmationModal(title, body, okButtonAction) {
    setDeletionConfirmationModalTitle(title);
    setDeletionConfirmationModalBody(body);
    setDeletionConfirmationModalOkButton(okButtonAction);
    $(`#${modalId}`).modal({
        backdrop: 'static'
    });
    $(`#${modalId}`).modal('show');
}

function hideDeletionConfirmationModal() {
    $(`#${modalId}`).modal('hide');
}


function setDeletionConfirmationModalTitle(title) {
    $(`#${modalTitleId}`).empty();
    $(`#${modalTitleId}`).append(title);
}

function setDeletionConfirmationModalBody(viewData) {
    $(`#${modalBodyId}`).html(viewData);
}

function setDeletionConfirmationModalOkButton(okButtonAction) {
    $(`#${modalOkButton}`).off('click').on('click', function () {
        hideDeletionConfirmationModal();
        okButtonAction();
    });
}