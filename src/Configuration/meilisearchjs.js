define(['globalize', 'loading', 'apphost'], function (globalize, loading, appHost) {
    'use strict';

    var pluginId = '8e4c7b5a-1234-5678-9abc-def012345678';

    function loadConfig(view) {
        loading.show();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            view.querySelector('.chkEnabled').checked = config.Enabled;
            view.querySelector('.txtMeilisearchUrl').value = config.MeilisearchUrl || '';
            view.querySelector('.txtMeilisearchApiKey').value = config.MeilisearchApiKey || '';
            view.querySelector('.txtIndexName').value = config.IndexName || '';
            view.querySelector('.chkAutoSync').checked = config.AutoSync;
            view.querySelector('.txtMaxResults').value = config.MaxSearchResults;
            view.querySelector('.txtMinLength').value = config.MinSearchTermLength;
            view.querySelector('.txtIncludeItemTypes').value = config.IncludeItemTypes || '';
            view.querySelector('.txtSearchableAttributes').value = config.SearchableAttributes || '';
            view.querySelector('.txtFilterableAttributes').value = config.FilterableAttributes || '';
            loading.hide();
        });
    }

    function saveConfig(view) {
        loading.show();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.Enabled = view.querySelector('.chkEnabled').checked;
            config.MeilisearchUrl = view.querySelector('.txtMeilisearchUrl').value;
            config.MeilisearchApiKey = view.querySelector('.txtMeilisearchApiKey').value;
            config.IndexName = view.querySelector('.txtIndexName').value;
            config.AutoSync = view.querySelector('.chkAutoSync').checked;
            config.MaxSearchResults = parseInt(view.querySelector('.txtMaxResults').value) || 100;
            config.MinSearchTermLength = parseInt(view.querySelector('.txtMinLength').value) || 1;
            config.IncludeItemTypes = view.querySelector('.txtIncludeItemTypes').value;
            config.SearchableAttributes = view.querySelector('.txtSearchableAttributes').value;
            config.FilterableAttributes = view.querySelector('.txtFilterableAttributes').value;

            ApiClient.updatePluginConfiguration(pluginId, config).then(function () {
                loading.hide();
                Dashboard.processPluginConfigurationUpdateResult();
            });
        });
    }

    return function (view, params) {
        view.querySelector('.meilisearchConfigForm').addEventListener('submit', function (e) {
            e.preventDefault();
            saveConfig(view);
            return false;
        });

        view.addEventListener('viewshow', function () {
            loadConfig(view);
        });
    };
});
