using System;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using KSP;
using System.IO;

using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KRnD
{

    // This class is used to store all relevant base-stats of a part used to calculate all other stats with
    // incrementel upgrades as well as a backup for resoting the original stats (eg after loading a savegame).
    public class PartStats
    {

        public float mass = 0;
        public Dictionary<string, KRnDVariant> kRnDVariants = null;
        public bool hasVariants = false;
        public string currentVariant;
        public float currentVariantMass = 0;
        public float variantBaseMass = 0;

        public List<float> maxFuelFlows = null;
        public List<FloatCurve> atmosphereCurves = null;
        public float torque = 0;
        public float chargeRate = 0;
        public float crashTolerance = 0;
        public double batteryCharge = 0;
        public Dictionary<String, double> generatorEfficiency = null; // Resource-Name, Rate
        public Dictionary<String, Dictionary<String, double>> converterEfficiency = null; // Converter Name, (Resource-Name, Ratio)
        public double chuteMaxTemp = 0;
        public double skinMaxTemp = 0;
        public double intMaxTemp = 0;
        public float fairingAreaMass = 0;
        public Dictionary<String, double> fuelCapacities = null; // Resource-Name, capacity
        public double fuelCapacitiesSum = 0; // Sum of all fuel capacities
        public double fissionPowerGeneration = 0; // From FissionGenerator
        //public double antPower = 0; // WIP
        public float harvester = 0;
        public double radiatorEfficiency = 0;


        public PartStats(Part part)
        {
            this.mass = part.mass;

            if (part.partInfo.variant != null)
            {
                kRnDVariants = KRnD.getVariants(part);
                currentVariant = part.partInfo.variant.Name;
                currentVariantMass = part.partInfo.variant.Mass;
                variantBaseMass = part.baseVariant.Mass;
            }
            if (kRnDVariants != null)
                hasVariants = true;
            else
            {
                currentVariantMass = 0;
                variantBaseMass = 0;
                hasVariants = false;
            }
            this.skinMaxTemp = part.skinMaxTemp;
            this.intMaxTemp = part.maxTemp;

            // There should only be one or the other, engines or RCS:
            List<ModuleEngines> engineModules = KRnD.getEngineModules(part);
            ModuleRCS rcsModule = KRnD.getRcsModule(part);
            if (engineModules != null)
            {
                this.maxFuelFlows = new List<float>();
                this.atmosphereCurves = new List<FloatCurve>();

                for (int i = 0; i < engineModules.Count; i++)
                {
                    ModuleEngines engineModule = engineModules[i];

                    this.maxFuelFlows.Add(engineModule.maxFuelFlow);

                    FloatCurve atmosphereCurve = new FloatCurve();
                    for (int i5 = 0; i5 < engineModule.atmosphereCurve.Curve.length; i5++)
                    {
                        Keyframe frame = engineModule.atmosphereCurve.Curve[i5];
                        try
                        {
                            atmosphereCurve.Add(frame.time, frame.value);
                        } catch (Exception ex)
                        {
                            Log.Error("Duplicate time (1) found when adding atmosphereCurve, part: " + part.partInfo.title + ", frame.time: " + frame.time);
                        }
                    }
                    this.atmosphereCurves.Add(atmosphereCurve);
                }
            }
            else if (rcsModule)
            {
                this.maxFuelFlows = new List<float>();
                this.atmosphereCurves = new List<FloatCurve>();

                this.maxFuelFlows.Add(rcsModule.thrusterPower);
                FloatCurve atmosphereCurve = new FloatCurve();
                for (int i = 0; i < rcsModule.atmosphereCurve.Curve.length; i++)
                {
                    Keyframe frame = rcsModule.atmosphereCurve.Curve[i];
                    try
                    {
                        atmosphereCurve.Add(frame.time, frame.value);
                    } catch (Exception ex)
                    {
                        Log.Error("Duplicate time (2) found when adding atmosphereCurve, part: " + part.partInfo.title + ", frame.time: " + frame.time);
                    }

                }
                this.atmosphereCurves.Add(atmosphereCurve);
            }

            ModuleReactionWheel reactionWheel = KRnD.getReactionWheelModule(part);
            if (reactionWheel)
            {
                this.torque = reactionWheel.RollTorque; // There is also pitch- and yaw-torque, but they should all be the same
            }

            // WIP
            //ModuleDataTransmitter dataTransmitter = KRnD.getDataTransmitterModule(part);
            //if (dataTransmitter)
            //{
            //    this.antPower = dataTransmitter.antennaPower;
            //}

            ModuleResourceHarvester resourceHarvester = KRnD.getResourceHarvesterModule(part);
            if (resourceHarvester)
            {
                this.harvester = resourceHarvester.Efficiency;
            }

            ModuleActiveRadiator activeRadiator = KRnD.getActiveRadiatorModule(part);
            if (activeRadiator)
            {
                this.radiatorEfficiency = activeRadiator.maxEnergyTransfer;
            }

            ModuleDeployableSolarPanel solarPanel = KRnD.getSolarPanelModule(part);
            if (solarPanel)
            {
                this.chargeRate = solarPanel.chargeRate;
            }

            ModuleWheelBase landingLeg = KRnD.getLandingLegModule(part);
            if (landingLeg)
            {
                this.crashTolerance = part.crashTolerance; // Every part has a crash tolerance, but we only want to improve landing legs.
            }

            PartResource electricCharge = KRnD.getChargeResource(part);
            if (electricCharge != null)
            {
                this.batteryCharge = electricCharge.maxAmount;
            }

            ModuleGenerator generator = KRnD.getGeneratorModule(part);
            if (generator != null)
            {
                generatorEfficiency = new Dictionary<String, double>();
                for (int i = 0; i < generator.resHandler.outputResources.Count; i++)
                {
                    ModuleResource outputResource = generator.resHandler.outputResources[i];
                    if (!generatorEfficiency.ContainsKey(outputResource.name))
                        generatorEfficiency.Add(outputResource.name, outputResource.rate);
                    else
                        ShowError(part, "outputResource", outputResource.name);

                }
            }

            PartModule fissionGenerator = KRnD.getFissionGeneratorModule(part);
            if (fissionGenerator != null)
            {
                fissionPowerGeneration = KRnD.getGenericModuleValue(fissionGenerator, "PowerGeneration");
            }

            // There might be different converter-modules in the same part with different names (eg for Fuel, Monopropellant, etc):
            List<ModuleResourceConverter> converterList = KRnD.getConverterModules(part);
            if (converterList != null)
            {
                converterEfficiency = new Dictionary<String, Dictionary<String, double>>();
                for (int i = 0; i < converterList.Count; i++)
                {
                    ModuleResourceConverter converter = converterList[i];

                    Dictionary<String, double> thisConverterEfficiency = new Dictionary<String, double>();
                    for (int i2 = 0; i2 < converter.outputList.Count; i2++)
                    {
                        ResourceRatio resourceRatio = converter.outputList[i2];

                        if (!thisConverterEfficiency.ContainsKey(resourceRatio.ResourceName))
                            thisConverterEfficiency.Add(resourceRatio.ResourceName, resourceRatio.Ratio);
                        else
                            ShowError(part, "resourceRatio", resourceRatio.ResourceName);

                    }
                    if (!converterEfficiency.ContainsKey(converter.ConverterName))
                        converterEfficiency.Add(converter.ConverterName, thisConverterEfficiency);
                    else
                        ShowError(part, "ConverterName", converter.ConverterName);
                }
            }

            ModuleParachute parachute = KRnD.getParachuteModule(part);
            if (parachute)
            {
                this.chuteMaxTemp = parachute.chuteMaxTemp;
            }

            ModuleProceduralFairing fairing = KRnD.getFairingModule(part);
            if (fairing)
            {
                this.fairingAreaMass = fairing.UnitAreaMass;
            }

            List<PartResource> fuelResources = KRnD.getFuelResources(part);
            if (fuelResources != null)
            {
                fuelCapacities = new Dictionary<string, double>();
                fuelCapacitiesSum = 0;
                for (int i = 0; i < fuelResources.Count; i++)
                {
                    PartResource fuelResource = fuelResources[i];

                    if (!fuelCapacities.ContainsKey(fuelResource.resourceName))
                    {
                        fuelCapacities.Add(fuelResource.resourceName, fuelResource.maxAmount);
                        fuelCapacitiesSum += fuelResource.maxAmount;
                    }
                    else
                    {
                        ShowError(part, "fuelResource", fuelResource.resourceName);
                    }
                }
            }
        }


        void ShowError(Part part, string type, string resName)
        {
            Log.Error("Part: " + part.partInfo.title + " (" + part.partInfo.configFileFullName + ") contains multiple definitions for " + type + ": " + resName);
        }
    }
}