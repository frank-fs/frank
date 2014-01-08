$(function () {
    var uri = 'api/cars';

    $.getJSON(uri)
        .done(function (data) {
            $.each(data, function (key, item) {
                $('<tr><td>' + (key + 1) + '</td><td>' + item.Make + '</td><td>' + item.Model + '</td></tr>')
                    .appendTo($('#cars tbody'));
            });
        });
});
