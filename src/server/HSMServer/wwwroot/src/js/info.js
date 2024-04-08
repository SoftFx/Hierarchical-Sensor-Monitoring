var infoModal = "info_modal";
var infoTitle = "info_title";
var infoBody = "info_body";
var infoOkButton = "info_okButton";


window.showInfoModal = function (title, body, okButton = "OK") {
    setInfoModalTitle(title);
    setInfoModalBody(body);
    setInfoModalOkButton(okButton);

    $(`#${infoModal}`).modal({
        backdrop: 'static'
    });
    $(`#${infoModal}`).modal('show');
}

window.hideInfoModal = function () {
    $(`#${infoModal}`).modal('hide');
}


function setInfoModalTitle(title) {
    $(`#${infoTitle}`).empty().append(title);
}

function setInfoModalBody(viewData) {
    $(`#${infoBody}`).html(viewData);
}

function setInfoModalOkButton(name) {
    $(`#${infoOkButton}`).off('click').on('click', () => {
        hideInfoModal();
    }).html(name);
}