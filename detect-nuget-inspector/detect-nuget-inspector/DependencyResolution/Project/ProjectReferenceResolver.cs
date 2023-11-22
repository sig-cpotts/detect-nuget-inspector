﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Synopsys.Detect.Nuget.Inspector.DependencyResolution.Nuget;
using Synopsys.Detect.Nuget.Inspector.Model;

namespace Synopsys.Detect.Nuget.Inspector.DependencyResolution.Project
{
    class ProjectReferenceResolver : DependencyResolver
    {

        private string ProjectPath;
        private NugetSearchService NugetSearchService;
        private HashSet<PackageId> CentrallyManagedPackages;
        private bool CheckVersionOverride;
        
        public ProjectReferenceResolver(string projectPath, NugetSearchService nugetSearchService)
        {
            ProjectPath = projectPath;
            NugetSearchService = nugetSearchService;
        }
        
        public ProjectReferenceResolver(string projectPath, NugetSearchService nugetSearchService, HashSet<PackageId> packages, bool checkVersionOverride): this(projectPath, nugetSearchService)
        {
            CentrallyManagedPackages = packages;
            CheckVersionOverride = checkVersionOverride;
        }
        
        public DependencyResult Process()
        {
            try
            {
                var tree = new NugetTreeResolver(NugetSearchService);

                Microsoft.Build.Evaluation.Project proj = new Microsoft.Build.Evaluation.Project(ProjectPath);

                List<NugetDependency> deps = new List<NugetDependency>();
                foreach (ProjectItem reference in proj.GetItemsIgnoringCondition("PackageReference"))
                {
                    bool containsPkg = CentrallyManagedPackages != null && CentrallyManagedPackages.Any(pkg => pkg.Name.Equals(reference.EvaluatedInclude));
                    
                    var versionMetaData = reference.Metadata.Where(meta => meta.Name == "Version").FirstOrDefault();
                    var versionOverrideMetaData = reference.Metadata.Where(meta => meta.Name == "VersionOverride").FirstOrDefault();
                    
                    if (containsPkg)
                    {
                        PackageId pkg = CentrallyManagedPackages.First(pkg => pkg.Name.Equals(reference.EvaluatedInclude));

                        if (CheckVersionOverride && versionOverrideMetaData != null)
                        {
                            addNugetDependency(reference.EvaluatedInclude,versionOverrideMetaData.EvaluatedValue,deps);
                        }
                        else if (!CheckVersionOverride && versionOverrideMetaData != null)
                        {
                            Console.WriteLine("The Central Package Version Overriding is disabled, please enable version override or remove VersionOverride tags from project");
                        }
                        else
                        {
                            addNugetDependency(reference.EvaluatedInclude,pkg.Version,deps);
                        }
                    }
                    else if (versionMetaData != null)
                    {
                        addNugetDependency(reference.EvaluatedInclude,versionMetaData.EvaluatedValue,deps);
                    }
                    else
                    {
                        Console.WriteLine("Framework dependency had no version, will not be included: " + reference.EvaluatedInclude);
                    }
                }

                foreach (ProjectItem reference in proj.GetItemsIgnoringCondition("Reference"))
                {
                    if (reference.Xml != null && !String.IsNullOrWhiteSpace(reference.Xml.Include) && reference.Xml.Include.Contains("Version="))
                    {

                        string packageInfo = reference.Xml.Include;

                        var artifact = packageInfo.Substring(0, packageInfo.IndexOf(","));

                        string versionKey = "Version=";
                        int versionKeyIndex = packageInfo.IndexOf(versionKey);
                        int versionStartIndex = versionKeyIndex + versionKey.Length;
                        string packageInfoAfterVersionKey = packageInfo.Substring(versionStartIndex);

                        string seapirater = ",";
                        string version;
                        if (packageInfoAfterVersionKey.Contains(seapirater))
                        {
                            int firstSeapirater = packageInfoAfterVersionKey.IndexOf(seapirater);
                            version = packageInfoAfterVersionKey.Substring(0, firstSeapirater);
                        }
                        else
                        {
                            version = packageInfoAfterVersionKey;
                        }

                        var dep = new NugetDependency(artifact, NuGet.Versioning.VersionRange.Parse(version));
                        deps.Add(dep);
                    }
                }
                ProjectCollection.GlobalProjectCollection.UnloadProject(proj);

                foreach (var dep in deps)
                {
                    tree.Add(dep);
                }

                var result = new DependencyResult()
                {
                    Success = true,
                    Packages = tree.GetPackageList(),
                    Dependencies = new List<PackageId>()
                };

                foreach (var package in result.Packages)
                {
                    var anyPackageReferences = result.Packages.Where(pkg => pkg.Dependencies.Contains(package.PackageId)).Any();
                    if (!anyPackageReferences)
                    {
                        result.Dependencies.Add(package.PackageId);
                    }
                }

                return result;
            }
            catch (InvalidProjectFileException e)
            {
                return new DependencyResult()
                {
                    Success = false
                };
            }
        }
        
        private void addNugetDependency(string include, string versionMetadata, List<NugetDependency> deps)
        {
            NuGet.Versioning.VersionRange version;
            if (NuGet.Versioning.VersionRange.TryParse(versionMetadata, out version))
            {
                var dep = new NugetDependency(include, version);
                deps.Add(dep);
            }
        }
    }
}
