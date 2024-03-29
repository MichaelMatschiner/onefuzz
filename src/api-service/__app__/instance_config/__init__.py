#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import InstanceConfigUpdate

from ..onefuzzlib.azure.nsg import is_onefuzz_nsg, list_nsgs, set_allowed
from ..onefuzzlib.config import InstanceConfig
from ..onefuzzlib.endpoint_authorization import call_if_user, can_modify_config
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    return ok(InstanceConfig.fetch())


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(InstanceConfigUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="instance_config_update")

    config = InstanceConfig.fetch()

    if not can_modify_config(req, config):
        return not_ok(
            Error(code=ErrorCode.INVALID_PERMISSION, errors=["unauthorized"]),
            context="instance_config_update",
        )

    update_nsg = False
    if request.config.proxy_nsg_config and config.proxy_nsg_config:
        request_config = request.config.proxy_nsg_config
        current_config = config.proxy_nsg_config
        if set(request_config.allowed_service_tags) != set(
            current_config.allowed_service_tags
        ) or set(request_config.allowed_ips) != set(current_config.allowed_ips):
            update_nsg = True

    config.update(request.config)
    config.save()

    # Update All NSGs
    if update_nsg:
        nsgs = list_nsgs()
        for nsg in nsgs:
            logging.info(
                "Checking if nsg: %s (%s) owned by OneFuzz" % (nsg.location, nsg.name)
            )
            if is_onefuzz_nsg(nsg.location, nsg.name):
                result = set_allowed(nsg.location, request.config.proxy_nsg_config)
                if isinstance(result, Error):
                    return not_ok(
                        Error(
                            code=ErrorCode.UNABLE_TO_CREATE,
                            errors=[
                                "Unable to update nsg %s due to %s"
                                % (nsg.location, result)
                            ],
                        ),
                        context="instance_config_update",
                    )

    return ok(config)


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
