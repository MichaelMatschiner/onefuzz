﻿using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;


public interface ISubnet {
    Async.Task<VirtualNetworkResource?> GetVnet(string vnetName);

    Async.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName);

    Async.Task<OneFuzzResultVoid> CreateVirtualNetwork(string resourceGroup, string name, Region region, NetworkConfig networkConfig);

    Async.Task<ResourceIdentifier?> GetSubnetId(string name, string subnetName);
}

public class Subnet : ISubnet {
    private readonly ICreds _creds;

    private readonly ILogTracer _logTracer;

    private readonly IOnefuzzContext _context;

    public Subnet(ICreds creds, ILogTracer logTracer, IOnefuzzContext context) {
        _creds = creds;
        _logTracer = logTracer;
        _context = context;
    }

    public async Task<OneFuzzResultVoid> CreateVirtualNetwork(string resourceGroup, string name, Region region, NetworkConfig networkConfig) {
        _logTracer.Info($"creating subnet - {resourceGroup:Tag:ResourceGroup} {name:Tag:Name} {region:Tag:Region}");

        var virtualNetParam = new VirtualNetworkData {
            Location = region,
        };

        virtualNetParam.AddressPrefixes.Add(networkConfig.AddressSpace);
        virtualNetParam.Subnets.Add(new SubnetData {
            Name = name,
            AddressPrefix = networkConfig.Subnet
        }
        );

        var onefuzzOwner = _context.ServiceConfiguration.OneFuzzOwner;
        if (!string.IsNullOrEmpty(onefuzzOwner)) {
            if (!virtualNetParam.Tags.TryAdd("OWNER", onefuzzOwner)) {
                _logTracer.Warning($"Failed to add tag 'OWNER':{onefuzzOwner:Tag:Owner} to virtual network {resourceGroup:Tag:ResourceGroup}:{name:Tag:Name}");
            }
        }

        try {
            _ = await _creds.GetResourceGroupResource().GetVirtualNetworks().CreateOrUpdateAsync(
                WaitUntil.Started,
                name,
                virtualNetParam);
        } catch (RequestFailedException ex) {
            _logTracer.Error($"network creation failed: {name:Tag:Name}:{region:Tag:Region} {ex.Message:Tag:Error}");
            return OneFuzzResultVoid.Error(
                ErrorCode.UNABLE_TO_CREATE_NETWORK,
                ex.ToString()
            );
        }

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName) {
        try {
            var vnet = await this.GetVnet(vnetName);

            if (vnet != null) {
                return await vnet.GetSubnetAsync(subnetName);
            }
            return null;
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            return null;
        }

    }

    public async Task<ResourceIdentifier?> GetSubnetId(string name, string subnetName) {
        var subnet = await this.GetSubnet(name, subnetName);
        if (subnet != null) {
            return subnet.Id;
        }

        return null;
    }

    public async Async.Task<VirtualNetworkResource?> GetVnet(string vnetName) {
        var resourceGroupId = _creds.GetResourceGroupResourceIdentifier();
        var response = await _creds.ArmClient.GetResourceGroupResource(resourceGroupId).GetVirtualNetworkAsync(vnetName);
        return response.Value;
    }
}
